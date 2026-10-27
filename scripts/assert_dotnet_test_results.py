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
    parser.add_argument(
        "--require-test",
        action="append",
        default=[],
        metavar="NAME",
        help="Require a passing test whose TRX name contains NAME; may be repeated",
    )
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

    results = root.findall(".//{*}UnitTestResult")
    for required in args.require_test:
        if not any(
            required in result.get("testName", "")
            and result.get("outcome", "").casefold() == "passed"
            for result in results
        ):
            print(f"Required .NET test did not pass: {required}", file=sys.stderr)
            return 1

    print(
        f"Verified .NET test results: {executed} executed, {passed} passed; "
        f"{len(args.require_test)} required tests passed."
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
