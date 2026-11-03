"""Check worker framing without downloading or importing an offline model."""
import io
import json
import pathlib
import runpy
import sys
import types
from unittest.mock import patch


class Utf8Buffer(io.StringIO):
    def __init__(self, value=""):
        super().__init__(value)
        self.flushes = 0

    def reconfigure(self, **kwargs):
        pass

    def flush(self):
        self.flushes += 1
        super().flush()


def check(stream):
    loads = []

    class Tokenizer:
        def __init__(self, **kwargs):
            pass

        def encode(self, text, **kwargs):
            return text.split()

        def decode(self, tokens):
            return " ".join(tokens)

    class Translator:
        def __init__(self, *args, **kwargs):
            loads.append(args)

        def translate_batch(self, parts, **kwargs):
            return [types.SimpleNamespace(hypotheses=[part]) for part in parts]

    payload = {"source": "en", "target": "zh", "texts": ["First paragraph", "Second paragraph"], "stream": stream}
    output = Utf8Buffer()
    worker = pathlib.Path(__file__).resolve().parents[1] / "src/ClariBar.App/Translation/translator.py"
    modules = {
        "ctranslate2": types.SimpleNamespace(Translator=Translator),
        "sentencepiece": types.SimpleNamespace(SentencePieceProcessor=Tokenizer),
    }
    with patch.dict(sys.modules, modules), patch.object(sys, "stdin", Utf8Buffer(json.dumps(payload))), patch.object(sys, "stdout", output), patch.object(sys, "argv", [str(worker), "unused-fixture-root"]):
        runpy.run_path(str(worker), run_name="__main__")
    assert len(loads) == 1, "The model must load once for the whole request."
    assert json.loads(output.getvalue()) == payload["texts"]


check(False)
check(True)
print("Offline worker passed: one model load and one complete JSON result; no local streaming.")
