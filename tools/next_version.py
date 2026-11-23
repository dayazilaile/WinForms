#!/usr/bin/env python3
"""Print the next WinForms version under the 23-patch rule; never publish or mutate files."""
import argparse
import json
from pathlib import Path
import re


def next_version(value, max_patch=23):
    if not re.fullmatch(r'(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)', value):
        raise ValueError('Expected major.minor.patch')
    major, minor, patch = map(int, value.split('.'))
    if patch > max_patch:
        raise ValueError('Patch exceeds the release-policy limit')
    return f'{major}.{minor + 1}.0' if patch == max_patch else f'{major}.{minor}.{patch + 1}'


if __name__ == '__main__':
    policy = json.loads((Path(__file__).resolve().parent.parent/'release-policy.json').read_text())
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('version', nargs='?', default=policy['current_version'])
    args = parser.parse_args()
    try:
        print(next_version(args.version, policy['max_patch']))
    except ValueError as error:
        parser.error(str(error))
