#!/usr/bin/env python3
"""Fail CI if a VSTest TRX report is missing or contains no passing tests."""

from __future__ import annotations

import argparse
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("report", type=Path, help="Path to the TRX results file")
    args = parser.parse_args()

    try:
        root = ET.parse(args.report).getroot()
        counters = root.find(".//{*}ResultSummary/{*}Counters")
        if counters is None:
            raise ValueError("TRX report has no ResultSummary/Counters")
        executed = int(counters.attrib["executed"])
        passed = int(counters.attrib["passed"])
    except (OSError, ET.ParseError, KeyError, ValueError) as exc:
        print(f"Cannot verify .NET test results: {exc}", file=sys.stderr)
        return 1

    if executed < 1 or passed < 1:
        print(
            f".NET test run must execute and pass at least one test "
            f"(executed={executed}, passed={passed}).",
            file=sys.stderr,
        )
        return 1

    print(f"Verified .NET test results: {executed} executed, {passed} passed.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
