/*
  ==========================================================================
  PhoneBootController  -  Mega 2560 edition
  ==========================================================================
  Developer : HaKDMoDz™
  Version   : 2.2.0
  Date      : 2026-10-09

  WHAT IT DOES
    An Arduino Mega 2560 acts as a "robot finger" for a phone motherboard with
    the battery removed. It can:
      1. Power the phone from a bench supply through a switched MOSFET (VCC).
      2. Fake the battery temperature/ID resistor so the phone will boot.
      3. Short a test point and/or the Vol+/Vol-/Power pads to GND.
      4. Connect or disconnect the phone's USB to the PC (clean unplug/replug).
      5. Read the phone's debug UART and forward it to the PC, and release
         the held lines when a chosen log string appears.
      6. Answer the TestPoint Trigger "Phone Jig" wiring checks
         (ident / pins / sense / selftest / uart loop / uart listen).

  CHANGES FROM 2.1.0
    - USB Host Shield / ADB removed. 2.1.0 included an adb.h that is not part
      of the USB Host Shield 2.0 library, so it never compiled. ADB is done
      from the PC by TestPoint Trigger's ADB / Fastboot module.
    - USB goes PC <-> off only. Tie the TS3USB221 S pin to GND (D1 = PC side).
      The shield VBUS switch is not fitted; D29 and D31 are now spare.
    - Same commands as the Uno build (PhoneBootController_Uno); only the pin
      numbers differ, and the phone UART stays on hardware Serial1 (D18/D19).

  HARDWARE
    Board    : Arduino Mega 2560 (5V logic, 16 MHz)
    USB mux  : TS3USB221 (or FSUSB42 / PI3USB102)  D = phone, D1 = PC, S = GND
    UART     : Serial1 (TX1 = D18, RX1 = D19) through a level shifter to 1.8V

  Compiled for arduino:avr:mega. Not yet bench-tested on real hardware.
  ==========================================================================
*/


// ==========================================================================
// STEP 1: PIN MAP
// ==========================================================================
// All jig pins sit in one block (D22..D30) so the wiring is easy to follow.
// D0/D1 belong to the USB-serial link to the PC; D18/D19 are Serial1.

// Four "open-drain" lines. The Mega never drives these HIGH. It either pulls
// the pad to GND (OUTPUT LOW) or lets go (INPUT = high impedance). Phone pads
// are often 1.8V, so driving them high could damage the phone.
const uint8_t PIN_TP      = 22;    // test point (EDL / BROM / EUB)
const uint8_t PIN_VOLUP   = 23;    // Vol+ pad
const uint8_t PIN_VOLDN   = 24;    // Vol- pad
const uint8_t PIN_PWR     = 25;    // Power pad

// Switch lines. HIGH turns the function ON. Each one drives a 2N7002 gate.
const uint8_t PIN_VCC_EN  = 26;    // phone VCC (bench supply -> P-FET -> Schottky -> BATT+)
const uint8_t PIN_BTEMP   = 27;    // fake battery-temp resistor to GND
const uint8_t PIN_VBUS_PC = 28;    // PC 5V -> P-FET -> Schottky -> phone VBUS

// Mux control. Goes through a 1k/2k divider (5V -> 3.3V).
const uint8_t PIN_USB_OE  = 30;    // TS3USB221 OE#: HIGH = isolate, LOW = connected

// Phone debug UART: hardware Serial1 (TX1 = D18 -> shifter -> phone RX,
// RX1 = D19 <- shifter <- phone TX).

// Optional sense inputs, each through a 10k/10k divider so 5 V reads as 2.5 V.
const uint8_t PIN_SENSE_VCC  = A0;   // phone VCC rail (after the Schottky)
const uint8_t PIN_SENSE_VBUS = A1;   // phone VBUS (after the Schottky)
const float   SENSE_RATIO    = 2.0;  // (10k + 10k) / 10k

const uint8_t PIN_LED     = 13;   // on-board LED: lit while a boot sequence runs
// Spare: D29, D31, D32..D49, A2..A15.

// ==========================================================================
// STEP 2: LINE MASKS
// ==========================================================================
#define K_TP   0x01
#define K_UP   0x02
#define K_DN   0x04
#define K_PWR  0x08
const uint8_t LINE_PIN[4] = { PIN_TP, PIN_VOLUP, PIN_VOLDN, PIN_PWR };   // bit i -> pin

enum { U_OFF = 0, U_PC = 1 };

// ==========================================================================
// STEP 3: MODE TABLE
// ==========================================================================
//   hold     : which lines are held low while power is applied
//   usbFirst : attach USB BEFORE power (MTK preloader needs this)
//   usbDelay : ms after VCC-on before USB attaches (ignored when usbFirst)
//   holdMs   : ms after VCC-on before the lines are released. If a 'trigger'
//              string is set, this becomes the MAXIMUM wait for that string.
struct Mode {
  const char* name;
  uint8_t  hold;
  bool     usbFirst;
  uint16_t usbDelay;
  uint16_t holdMs;
};

Mode MODES[] = {
  // name         hold                 first  usbDly holdMs
  { "edl",        K_TP,                false,   400,  2500 },  // Qualcomm 9008
  { "brom",       K_TP,                false,   400,  2500 },  // MTK BootROM
  { "eub",        K_TP,                false,   400,  2500 },  // Samsung Exynos USB Boot
  { "preloader",  0,                   true,      0,     0 },  // MTK preloader window
  { "fastboot",   K_DN | K_PWR,        false,  1500,  5000 },
  { "bootloader", K_UP | K_DN | K_PWR, false,  1500,  5000 },
  { "download",   K_UP | K_DN,         false,   500,  4000 },  // Samsung Odin
  { "recovery",   K_UP | K_PWR,        false,  1500,  5000 },
  { "normal",     K_PWR,               false,  3000,  3000 }   // boot Android, USB to PC
};
const uint8_t NMODES = sizeof(MODES) / sizeof(MODES[0]);

// ==========================================================================
// STEP 4: STATE MACHINE VARIABLES
// ==========================================================================
enum State : uint8_t { S_IDLE, S_ISOLATE, S_ASSERT, S_USB_FIRST, S_VCC_ON, S_HOLD };
State    state  = S_IDLE;
uint8_t  cur    = 0;
uint8_t  curUsb = U_OFF;
uint32_t tState = 0;
uint32_t tVcc   = 0;
const uint16_t DISCHARGE_MS = 400;

// ==========================================================================
// STEP 5: UART VARIABLES
// ==========================================================================
#define phone Serial1
uint32_t uartBaud = 115200;
bool     uartOn   = false;
char     uartLine[112];
uint8_t  uartLen  = 0;
uint32_t uartLastByte = 0;
uint32_t uartJunk = 0;
char     trigStr[32] = "";
bool     trigHit = false;

// ==========================================================================
// STEP 6: LINE, POWER AND USB HELPERS
// ==========================================================================
inline void lineHold(uint8_t p)    { digitalWrite(p, LOW); pinMode(p, OUTPUT); }   // LOW before OUTPUT: no glitch high
inline void lineRelease(uint8_t p) { pinMode(p, INPUT); }                          // high impedance, never HIGH

void applyHold(uint8_t mask) {
  for (uint8_t i = 0; i < 4; i++) {
    if (mask & (1 << i)) lineHold(LINE_PIN[i]); else lineRelease(LINE_PIN[i]);
  }
}

void vccOn()  { digitalWrite(PIN_VCC_EN, HIGH); tVcc = millis(); }
void vccOff() { digitalWrite(PIN_VCC_EN, LOW); }

// VBUS off -> data isolate -> wait -> data on -> VBUS on, so the phone sees a
// clean unplug and replug.
void usbSet(uint8_t t) {
  digitalWrite(PIN_VBUS_PC, LOW);
  digitalWrite(PIN_USB_OE, HIGH);
  delay(80);
  curUsb = t;
  if (t == U_OFF) return;
  digitalWrite(PIN_USB_OE, LOW);
  delay(20);
  digitalWrite(PIN_VBUS_PC, HIGH);
}

void allSafe() { applyHold(0); vccOff(); usbSet(U_OFF); }

// ==========================================================================
// STEP 7: UART ENGINE
// ==========================================================================
// Estimate how far the real baud rate is from the requested one. The Mega's
// 16 MHz clock cannot make every rate: 921600 comes out about +8.5% off and
// will show garbage, while 115200 is about +2.1% and works.
long uartErrorTenths(uint32_t baud) {
  uint32_t setting = (F_CPU / 4 / baud - 1) / 2;       // HardwareSerial U2X formula
  uint32_t actual  = F_CPU / (8UL * (setting + 1));
  return ((long)actual - (long)baud) * 1000L / (long)baud;
}

void uartBegin() {
  phone.begin(uartBaud);
  uartOn = true;
  uartLen = 0; uartLine[0] = 0; uartJunk = 0;
}

void uartEnd() { phone.end(); uartOn = false; }

void uartFlushLine() {
  if (uartLen) {
    Serial.print(F("[uart] ")); Serial.println(uartLine);
    uartLen = 0; uartLine[0] = 0;
  }
}

void serviceUart() {
  if (!uartOn) return;
  while (phone.available()) {
    char c = phone.read();
    uartLastByte = millis();
    if (c == '\r') continue;
    if (c == '\n') { uartFlushLine(); continue; }
    if ((c < 32 || c > 126) && c != '\t') { uartJunk++; continue; }   // junk usually means wrong baud
    if (uartLen < sizeof(uartLine) - 1) { uartLine[uartLen++] = c; uartLine[uartLen] = 0; }
    else uartFlushLine();
    if (trigStr[0] && !trigHit && strstr(uartLine, trigStr)) trigHit = true;
  }
  if (uartLen && millis() - uartLastByte > 200) uartFlushLine();   // prompts with no newline
}

// ==========================================================================
// STEP 8: THE STATE MACHINE
// ==========================================================================
void goTo(State s) { state = s; tState = millis(); }

void startMode(uint8_t i) {
  cur = i;
  Serial.print(F(">> start: ")); Serial.println(MODES[i].name);
  trigHit = false;
  if (trigStr[0] && !uartOn) { uartBegin(); Serial.println(F("[uart] enabled for trigger")); }
  allSafe();
  digitalWrite(PIN_LED, HIGH);
  goTo(S_ISOLATE);
}

void runSM() {
  if (state == S_IDLE) return;
  const Mode& m = MODES[cur];
  uint32_t dt = millis() - tState;
  switch (state) {
    case S_ISOLATE:   if (dt >= DISCHARGE_MS) { applyHold(m.hold); goTo(S_ASSERT); } break;
    case S_ASSERT:
      if (dt >= 50) {
        if (m.usbFirst) { usbSet(U_PC); goTo(S_USB_FIRST); }
        else            { vccOn();      goTo(S_VCC_ON); }
      }
      break;
    case S_USB_FIRST: if (dt >= 100) { vccOn(); goTo(S_VCC_ON); } break;
    case S_VCC_ON:
      if (m.usbFirst) goTo(S_HOLD);
      else if (dt >= m.usbDelay) { usbSet(U_PC); goTo(S_HOLD); }
      break;
    case S_HOLD: {
      bool timedOut  = (millis() - tVcc >= m.holdMs);
      bool triggered = (trigStr[0] && trigHit);
      if (timedOut || triggered) {
        applyHold(0);
        digitalWrite(PIN_LED, LOW);
        Serial.print(F("[done] ")); Serial.print(m.name);
        Serial.print(triggered ? F(" (UART trigger)") : F(" (timer)"));
        Serial.println(F(": USB -> PC. Check the PC for the device."));
        goTo(S_IDLE);
      }
      break;
    }
    default: break;
  }
}

// ==========================================================================
// STEP 9: WIRING VERIFICATION (answers the PC app's Wiring tab)
// ==========================================================================
float readRail(uint8_t pin) {
  analogRead(pin);                                   // throw the first away so the ADC settles
  uint32_t acc = 0;
  for (uint8_t i = 0; i < 8; i++) acc += analogRead(pin);
  return (acc / 8.0f) * (5.0f / 1023.0f) * SENSE_RATIO;
}

bool isOut(uint8_t p) { return *portModeRegister(digitalPinToPort(p)) & digitalPinToBitMask(p); }

void cmdIdent() {
  Serial.print(F("IDENT,PBJ,2.2.0,MEGA2560,shield=0,uart=")); Serial.print(uartOn ? 1 : 0);
  Serial.print(F(",baud=")); Serial.println(uartBaud);
}

void cmdPins() {
  const char* nm[4] = { "TP", "UP", "DN", "PWR" };
  Serial.print(F("PINS"));
  for (uint8_t i = 0; i < 4; i++) {
    Serial.print(','); Serial.print(nm[i]); Serial.print('=');
    Serial.print(isOut(LINE_PIN[i]) ? F("held") : F("rel"));
    Serial.print(F("/pad")); Serial.print(digitalRead(LINE_PIN[i]));
  }
  Serial.print(F(",VCC="));      Serial.print(digitalRead(PIN_VCC_EN));
  Serial.print(F(",BTEMP="));    Serial.print(digitalRead(PIN_BTEMP));
  Serial.print(F(",VBUS_PC=")); Serial.print(digitalRead(PIN_VBUS_PC));
  Serial.print(F(",VBUS_SH=0,OE=")); Serial.print(digitalRead(PIN_USB_OE));
  Serial.print(F(",SEL=0,USB="));    Serial.println(curUsb == U_PC ? F("pc") : F("off"));
}

void cmdSense() {
  Serial.print(F("SENSE,VCC=")); Serial.print(readRail(PIN_SENSE_VCC), 2);
  Serial.print(F(",VBUS="));     Serial.println(readRail(PIN_SENSE_VBUS), 2);
}

void stLine(const __FlashStringHelper* name, float off, float on, float minOn) {
  Serial.print(F("ST,")); Serial.print(name);
  Serial.print(F(",off=")); Serial.print(off, 2);
  Serial.print(F(",on="));  Serial.print(on, 2);
  if (off > 0.6f)      Serial.println(F(",FAIL_LEAK"));
  else if (on < minOn) Serial.println(F(",FAIL_NO_VOLTAGE"));
  else                 Serial.println(F(",PASS"));
}

// Switch each supply on in turn and check the sense input saw it.
// Run with the phone unplugged or a dummy load on the VCC lead.
void cmdSelftest() {
  state = S_IDLE; allSafe();
  delay(DISCHARGE_MS);
  float off, on;
  off = readRail(PIN_SENSE_VCC);  digitalWrite(PIN_VCC_EN, HIGH);  delay(150);
  on  = readRail(PIN_SENSE_VCC);  digitalWrite(PIN_VCC_EN, LOW);   stLine(F("vcc"), off, on, 2.5f);
  delay(DISCHARGE_MS);
  off = readRail(PIN_SENSE_VBUS); digitalWrite(PIN_VBUS_PC, HIGH); delay(100);
  on  = readRail(PIN_SENSE_VBUS); digitalWrite(PIN_VBUS_PC, LOW);  stLine(F("vbus_pc"), off, on, 4.0f);
  Serial.println(F("ST,done"));
}

// Needs D18 (TX1) jumpered to D19 (RX1) on the Mega side, level shifter unplugged.
void cmdUartLoop() {
  bool was = uartOn; if (!was) uartBegin();
  while (phone.available()) phone.read();
  const char* t = "PBJ-LOOP-0123456789";
  phone.print(t); phone.flush(); delay(60);
  char buf[32]; uint8_t n = 0;
  while (phone.available() && n < sizeof(buf) - 1) buf[n++] = phone.read();
  buf[n] = 0;
  Serial.print(F("UARTLOOP,")); Serial.print(!strcmp(buf, t) ? F("PASS") : n ? F("GARBLED") : F("NOECHO"));
  Serial.print(F(",rx=")); Serial.println(n);
  uartLen = 0; if (!was) uartEnd();
}

// Count what the phone sends for <ms>. Bytes seen but mostly junk = wrong baud.
void cmdUartListen(uint16_t ms) {
  if (ms > 10000) ms = 10000;
  bool was = uartOn; if (!was) uartBegin();
  uint32_t t0 = millis(); uint16_t n = 0, junk = 0;
  while (millis() - t0 < ms) {
    while (phone.available()) {
      char c = phone.read(); n++;
      if ((c < 32 || c > 126) && c != '\r' && c != '\n' && c != '\t') junk++;
    }
  }
  Serial.print(F("UARTLISTEN,bytes=")); Serial.print(n);
  Serial.print(F(",junk=")); Serial.println(junk);
  uartLen = 0; if (!was) uartEnd();
}

// ==========================================================================
// STEP 10: SERIAL COMMAND SHELL (PC side, 115200 baud)
// ==========================================================================
void printHelp() {
  Serial.println(F("--- PhoneBootController 2.2.0 (Mega) | HaKDMoDz™ | 2026-10-09 ---"));
  Serial.println(F("modes   : edl brom eub preloader fastboot bootloader download recovery normal"));
  Serial.println(F("off                         everything off, lines released"));
  Serial.println(F("status                      mode table, pin and UART state"));
  Serial.println(F("usb pc|off                  connect/disconnect the phone USB"));
  Serial.println(F("vcc on|off                  phone VCC switch"));
  Serial.println(F("btemp on|off                fake battery-temp resistor"));
  Serial.println(F("key tp|up|dn|pwr hold|rel   manual line control"));
  Serial.println(F("tune <mode> <mask> <usbDelayMs> <holdMs>   mask: TP=1 UP=2 DN=4 PWR=8"));
  Serial.println(F("uart on|off|baud <n>|send <text>|loop|listen <ms>"));
  Serial.println(F("trigger <text>|off          release held lines when <text> appears on the UART"));
  Serial.println(F("ident | pins | sense | selftest   wiring checks"));
}

void printStatus() {
  Serial.println(F("name        mask usbDly holdMs usbFirst"));
  for (uint8_t i = 0; i < NMODES; i++) {
    Serial.print(MODES[i].name); Serial.print(F("\t0x")); Serial.print(MODES[i].hold, HEX);
    Serial.print('\t'); Serial.print(MODES[i].usbDelay);
    Serial.print('\t'); Serial.print(MODES[i].holdMs);
    Serial.print('\t'); Serial.println(MODES[i].usbFirst ? F("yes") : F("no"));
  }
  Serial.print(F("VCC="));  Serial.print(digitalRead(PIN_VCC_EN));
  Serial.print(F(" USB="));  Serial.print(curUsb == U_PC ? F("PC") : F("off"));
  Serial.print(F(" busy=")); Serial.println(state != S_IDLE);
  Serial.print(F("UART ")); Serial.print(uartOn ? F("on ") : F("off ")); Serial.print(uartBaud);
  Serial.print(F(" baud, error ")); Serial.print(uartErrorTenths(uartBaud) / 10.0, 1);
  Serial.print(F("%, junk bytes ")); Serial.println(uartJunk);
  Serial.print(F("trigger: ")); Serial.println(trigStr[0] ? trigStr : "(none)");
}

int findMode(const char* n) {
  for (uint8_t i = 0; i < NMODES; i++) if (!strcasecmp(n, MODES[i].name)) return i;
  return -1;
}

void handle(char* ln) {
  // Free-text commands first so their case is preserved.
  if (!strncasecmp(ln, "adb", 3)) {
    Serial.println(F("No ADB on the jig - use TestPoint Trigger's ADB / Fastboot module."));
    return;
  }
  if (!strncasecmp(ln, "uart send ", 10)) {
    if (!uartOn) { Serial.println(F("uart is off")); return; }
    phone.print(ln + 10); phone.print("\r\n");
    Serial.print(F("[uart] >> ")); Serial.println(ln + 10);
    return;
  }
  if (!strncasecmp(ln, "trigger ", 8)) {
    const char* a = ln + 8;
    if (!strcasecmp(a, "off")) { trigStr[0] = 0; Serial.println(F("trigger cleared")); }
    else { strncpy(trigStr, a, sizeof(trigStr) - 1); trigStr[sizeof(trigStr) - 1] = 0;
           Serial.print(F("trigger set: ")); Serial.println(trigStr); }
    return;
  }

  char* sv;
  char* t0 = strtok_r(ln, " ", &sv);
  if (!t0) return;
  char* t1 = strtok_r(NULL, " ", &sv);
  char* t2 = strtok_r(NULL, " ", &sv);
  char* t3 = strtok_r(NULL, " ", &sv);
  char* t4 = strtok_r(NULL, " ", &sv);

  if (!strcasecmp(t0, "help"))          printHelp();
  else if (!strcasecmp(t0, "status"))   printStatus();
  else if (!strcasecmp(t0, "ident"))    cmdIdent();
  else if (!strcasecmp(t0, "pins"))     cmdPins();
  else if (!strcasecmp(t0, "sense"))    cmdSense();
  else if (!strcasecmp(t0, "selftest")) cmdSelftest();
  else if (!strcasecmp(t0, "off")) { state = S_IDLE; allSafe(); digitalWrite(PIN_LED, LOW); Serial.println(F("all off")); }
  else if (!strcasecmp(t0, "usb") && t1) {
    if (!strcasecmp(t1, "shield")) { Serial.println(F("no host shield in 2.2.0 - USB goes to the PC or off")); return; }
    state = S_IDLE;
    usbSet(!strcasecmp(t1, "pc") ? U_PC : U_OFF);
    Serial.println(F("ok"));
  }
  else if (!strcasecmp(t0, "vcc") && t1)   { if (!strcasecmp(t1, "on")) vccOn(); else vccOff(); Serial.println(F("ok")); }
  else if (!strcasecmp(t0, "btemp") && t1) { digitalWrite(PIN_BTEMP, !strcasecmp(t1, "on")); Serial.println(F("ok")); }
  else if (!strcasecmp(t0, "key") && t1 && t2) {
    uint8_t p = !strcasecmp(t1, "tp") ? PIN_TP : !strcasecmp(t1, "up") ? PIN_VOLUP :
                !strcasecmp(t1, "dn") ? PIN_VOLDN : PIN_PWR;
    if (!strcasecmp(t2, "hold")) lineHold(p); else lineRelease(p);
    Serial.println(F("ok"));
  }
  else if (!strcasecmp(t0, "tune") && t1 && t2 && t3 && t4) {
    int i = findMode(t1);
    if (i < 0) { Serial.println(F("unknown mode")); return; }
    MODES[i].hold     = (uint8_t)strtol(t2, NULL, 0);
    MODES[i].usbDelay = (uint16_t)atoi(t3);
    MODES[i].holdMs   = (uint16_t)atoi(t4);
    Serial.println(F("tuned (RAM only, lost on reset)"));
  }
  else if (!strcasecmp(t0, "uart")) {
    if (t1 && !strcasecmp(t1, "on"))       { uartBegin(); Serial.println(F("uart on")); }
    else if (t1 && !strcasecmp(t1, "off")) { uartEnd();   Serial.println(F("uart off")); }
    else if (t1 && !strcasecmp(t1, "baud") && t2) {
      uint32_t b = strtoul(t2, NULL, 10);
      if (b < 1200 || b > 1000000UL) { Serial.println(F("baud out of range")); return; }
      uartBaud = b;
      long e = uartErrorTenths(b);
      Serial.print(F("baud ")); Serial.print(b); Serial.print(F(", real-rate error ")); Serial.print(e / 10.0, 1); Serial.println('%');
      if (e > 25 || e < -25) Serial.println(F("WARNING: error over 2.5%, logs will be garbled. Use 115200 or a USB-UART adapter."));
      if (uartOn) { uartEnd(); uartBegin(); }
    }
    else if (t1 && !strcasecmp(t1, "loop"))   cmdUartLoop();
    else if (t1 && !strcasecmp(t1, "listen")) cmdUartListen(t2 ? (uint16_t)atoi(t2) : 2000);
    else Serial.println(F("uart on|off|baud <n>|send <text>|loop|listen <ms>"));
  }
  else {
    int i = findMode(t0);
    if (i >= 0) startMode(i); else Serial.println(F("? type help"));
  }
}

char    lineBuf[96];
uint8_t lineLen = 0;

void readSerial() {
  while (Serial.available()) {
    char c = Serial.read();
    if (c == '\r' || c == '\n') {
      if (lineLen) { lineBuf[lineLen] = 0; handle(lineBuf); lineLen = 0; }
    } else if (lineLen < sizeof(lineBuf) - 1) {
      lineBuf[lineLen++] = c;
    }
  }
}

// ==========================================================================
// STEP 11: SETUP
// ==========================================================================
void setup() {
  // Safe levels FIRST: phone unpowered, USB isolated, battery-temp resistor
  // connected, open-drain lines released.
  digitalWrite(PIN_VCC_EN, LOW);   pinMode(PIN_VCC_EN, OUTPUT);
  digitalWrite(PIN_VBUS_PC, LOW);  pinMode(PIN_VBUS_PC, OUTPUT);
  digitalWrite(PIN_USB_OE, HIGH);  pinMode(PIN_USB_OE, OUTPUT);
  digitalWrite(PIN_BTEMP, HIGH);   pinMode(PIN_BTEMP, OUTPUT);
  digitalWrite(PIN_LED, LOW);      pinMode(PIN_LED, OUTPUT);
  applyHold(0);

  Serial.begin(115200);
  Serial.println(F("PhoneBootController v2.2.0 (Mega) | Developer: HaKDMoDz™ | 2026-10-09"));
  Serial.println(F("type 'help'"));
}

// ==========================================================================
// STEP 12: MAIN LOOP
// ==========================================================================
void loop() {
  readSerial();
  serviceUart();
  runSM();
}
