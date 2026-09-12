"""Compares the harvest file against the dictionary and prints what still needs translating.

Usage:
    python tools/collect.py [--missing PATH] [--dict PATH] [--write PATH]

Defaults:
    missing = %APPDATA%\ETS2LA\zh-hans-missing.txt   (written by the plugin)
    dict    = %APPDATA%\ETS2LA\zh-hans-fix.json      (the live dictionary)

The output is a JSON object with the still-missing source strings as keys and empty
values, ready to be filled in and merged into the dictionary.
"""

import argparse
import json
import os
import re
import sys


def default_missing():
    return os.path.join(os.environ.get("APPDATA", ""), "ETS2LA", "zh-hans-missing.txt")


def default_dict():
    return os.path.join(os.environ.get("APPDATA", ""), "ETS2LA", "zh-hans-fix.json")


def is_skippable(s):
    """Format-only strings, units and bare identifiers don't need translating."""
    t = s.strip()
    if not t:
        return True
    if re.fullmatch(r"[\{\}\d\s\.\,\:%/\\\-\+\|\(\)\[\]]+", t):
        return True
    if re.fullmatch(r"[A-Za-z]{1,3}", t):
        return True
    if re.fullmatch(r"(km/h|mph|m/s|PID|ACC|AEB|ETS2LA( overlay)?)", t, re.I):
        return True
    if not re.search(r"[A-Za-z\u4e00-\u9fff]", t):
        return True
    return False


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--missing", default=default_missing())
    ap.add_argument("--dict", dest="dict_path", default=default_dict())
    ap.add_argument("--write", default=None, help="write the to-translate template to this path")
    args = ap.parse_args()

    if not os.path.exists(args.missing):
        sys.exit(f"harvest file not found: {args.missing}\n(run ETS2LA with the plugin installed and browse its UI first)")

    harvested = [l for l in open(args.missing, encoding="utf-8", errors="replace").read().splitlines() if l.strip()]
    translations = {}
    if os.path.exists(args.dict_path):
        translations = json.load(open(args.dict_path, encoding="utf-8"))

    unique = sorted(set(harvested))
    todo = [s for s in unique if s not in translations and not is_skippable(s)]
    covered = [s for s in unique if s in translations]

    print(f"harvested: {len(harvested)} lines / {len(unique)} unique")
    print(f"already translated: {len(covered)}")
    print(f"still to translate: {len(todo)}")
    print(f"dictionary size: {len(translations)}")

    if todo:
        print("\n--- to translate ---")
        for s in todo:
            print(f"  {s}")
        if args.write:
            with open(args.write, "w", encoding="utf-8") as f:
                json.dump({s: "" for s in todo}, f, ensure_ascii=False, indent=1)
            print(f"\ntemplate written to {args.write}")


if __name__ == "__main__":
    main()
