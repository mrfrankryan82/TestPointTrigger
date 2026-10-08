// Mobile Surgery - offline voice command listener
// Developer: HaKDMoDz™ · v1.0.0 · 2026-09-26
using System;
using System.Collections.Generic;
using System.Linq;
using System.Speech.Recognition;

namespace TestPointTrigger.Modules
{
    /// <summary>
    /// Listens on the default microphone for a small set of command phrases
    /// using the built-in Windows recogniser (System.Speech) - fully offline.
    ///
    /// A command-only grammar forces ANY speech into the closest command, so
    /// normal talking at the bench would fire it. A dictation grammar is loaded
    /// alongside as a "sink": ordinary speech matches dictation instead, and
    /// only results from the command grammar are raised as Heard.
    ///
    /// Events are raised on a recogniser thread; marshal before touching UI.
    /// </summary>
    internal sealed class VoiceTrigger : IDisposable
    {
        private SpeechRecognitionEngine _engine;
        private Grammar _commands;

        /// <summary>A command phrase was recognised at or above MinConfidence.</summary>
        public event Action<string, float> Heard;

        /// <summary>Diagnostic / status text for the log.</summary>
        public event Action<string> Info;

        public float MinConfidence { get; set; } = 0.70f;
        public bool IsListening => _engine != null;

        public bool Start(IEnumerable<string> phrases)
        {
            Stop();

            var list = phrases
                .Select(p => p.Trim())
                .Where(p => p.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (list.Length == 0) { Info?.Invoke("Voice: no command phrases set."); return false; }

            var recognizers = SpeechRecognitionEngine.InstalledRecognizers();
            var info = recognizers.FirstOrDefault(r => r.Culture.TwoLetterISOLanguageName == "en")
                       ?? recognizers.FirstOrDefault();
            if (info == null)
            {
                Info?.Invoke("Voice: no speech recogniser installed. Add one in Settings > Time & language > Speech.");
                return false;
            }

            try
            {
                _engine = new SpeechRecognitionEngine(info);

                _commands = new Grammar(new GrammarBuilder(new Choices(list)) { Culture = info.Culture })
                {
                    Name = "commands",
                    Priority = 10
                };
                _engine.LoadGrammar(_commands);

                try { _engine.LoadGrammar(new DictationGrammar { Name = "sink", Priority = 0 }); }
                catch { Info?.Invoke("Voice: dictation sink unavailable - expect more false triggers."); }

                _engine.SpeechRecognized += OnRecognized;
                _engine.SetInputToDefaultAudioDevice();
                _engine.RecognizeAsync(RecognizeMode.Multiple);

                Info?.Invoke("Voice: listening (" + info.Culture.Name + ") for: " + string.Join(", ", list) + ".");
                return true;
            }
            catch (Exception ex)
            {
                Info?.Invoke("Voice: could not start - " + ex.Message +
                             " (is a microphone connected and allowed in Windows privacy settings?)");
                Stop();
                return false;
            }
        }

        private void OnRecognized(object sender, SpeechRecognizedEventArgs e)
        {
            var r = e.Result;
            if (r?.Grammar == null || !ReferenceEquals(r.Grammar, _commands)) return; // ordinary speech

            if (r.Confidence >= MinConfidence) Heard?.Invoke(r.Text, r.Confidence);
            else Info?.Invoke($"Voice: heard \"{r.Text}\" at {r.Confidence:P0} - below {MinConfidence:P0}, ignored.");
        }

        public void Stop()
        {
            var eng = _engine;
            _engine = null;
            _commands = null;
            if (eng == null) return;
            try
            {
                eng.SpeechRecognized -= OnRecognized;
                eng.RecognizeAsyncCancel();
            }
            catch { }
            finally { eng.Dispose(); }
        }

        public void Dispose() => Stop();
    }
}
