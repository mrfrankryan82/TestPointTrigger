import sys
ROOT = r"C:\Users\User\source\repos\TestPointTrigger\TestPointTrigger" + "\\"

def load(rel):
    with open(ROOT + rel, "r", encoding="utf-8-sig", newline="") as f: return f.read()

def save(rel, text):
    with open(ROOT + rel, "w", encoding="utf-8-sig", newline="") as f: f.write(text)

def between(text, start, end, new):
    """Replace text[start-marker .. end-marker) with new (end marker kept)."""
    i = text.find(start)
    if i < 0: raise SystemExit("start marker not found: " + start[:60])
    j = text.find(end, i + len(start))
    if j < 0: raise SystemExit("end marker not found: " + end[:60])
    return text[:i] + new + text[j:]

def replace1(text, old, new):
    n = text.count(old)
    if n != 1: raise SystemExit("expected 1 match, got %d: %s" % (n, old[:70]))
    return text.replace(old, new)

def crlf(s): return s.replace("\r\n", "\n").replace("\n", "\r\n")
