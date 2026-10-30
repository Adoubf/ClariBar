"""Isolated offline inference worker. Text crosses stdin/stdout, never a URL."""
import json
import os
import pathlib
import sys

os.environ["OMP_NUM_THREADS"] = "4"
import ctranslate2
import sentencepiece

sys.stdin.reconfigure(encoding="utf-8")
sys.stdout.reconfigure(encoding="utf-8")
request = json.load(sys.stdin)
root = pathlib.Path(sys.argv[1]) / "models" / (request["source"] + "_" + request["target"])
tokenizer = sentencepiece.SentencePieceProcessor(model_file=str(root / "sentencepiece.model"))
translator = ctranslate2.Translator(str(root / "model"), device="cpu", compute_type="int8", inter_threads=1, intra_threads=4)
output = []
for text in request["texts"]:
    tokens = tokenizer.encode(text, out_type=str)
    # Split long OCR lines explicitly instead of silently truncating source tokens.
    parts = [tokens[i:i + 220] for i in range(0, len(tokens), 220)]
    results = translator.translate_batch(parts, beam_size=3, max_decoding_length=512, max_input_length=0)
    output.append(" ".join(tokenizer.decode(result.hypotheses[0]) for result in results))
print(json.dumps(output, ensure_ascii=False))
