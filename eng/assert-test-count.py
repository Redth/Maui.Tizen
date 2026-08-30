#!/usr/bin/env python3
"""Assert that a test run actually executed at least a minimum number of tests.

An exit code alone is not sufficient evidence that a test suite ran. Test discovery over
the Essentials assembly walks assembly-level attributes across the loaded closure, and
Tizen.NUI's [XmlnsDefinition] constructor P/Invokes and throws when no Tizen device is
present. On the xunit v2 runner that aborted discovery part way through a class and
silently dropped the remaining tests while still reporting success.

The count is read from the runner's JUnit report rather than from its console output.
Console rendering is not a stable contract - an earlier version of this check scraped
stdout, passed locally, and failed on the CI runner.
"""

import sys
import xml.etree.ElementTree as ET
from pathlib import Path


def main(argv: list[str]) -> int:
    if len(argv) != 3:
        print(f"usage: {Path(argv[0]).name} <junit-report.xml> <minimum>", file=sys.stderr)
        return 2

    report = Path(argv[1])
    try:
        minimum = int(argv[2])
    except ValueError:
        print(f"minimum must be an integer, got {argv[2]!r}", file=sys.stderr)
        return 2

    if not report.is_file():
        print(f"no test report at '{report}' - the runner did not produce one")
        return 1

    try:
        root = ET.parse(report).getroot()
    except ET.ParseError as error:
        print(f"could not parse '{report}': {error}")
        return 1

    # Count <testcase> elements rather than trusting the summary attribute, so a report
    # whose header disagrees with its body cannot pass.
    total = len(list(root.iter("testcase")))

    if total < minimum:
        print(f"test discovery regressed: ran {total}, expected at least {minimum}")
        return 1

    failures = len(list(root.iter("failure"))) + len(list(root.iter("error")))
    if failures:
        print(f"{failures} test(s) reported a failure in '{report}'")
        return 1

    print(f"ran {total} tests")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
