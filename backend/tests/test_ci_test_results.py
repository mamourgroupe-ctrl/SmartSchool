"""The CI safety gate must reject missing, empty, or invalid .NET test runs."""

from pathlib import Path
import subprocess
import sys

import pytest


SCRIPT = Path(__file__).resolve().parents[2] / "scripts" / "assert_dotnet_test_results.py"


def _run_gate(report: Path, *extra_args: str) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        [sys.executable, str(SCRIPT), str(report), *extra_args],
        capture_output=True,
        text=True,
        check=False,
    )


def _trx(executed: str, passed: str, results: str = "") -> str:
    return (
        '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">'
        f'<Results>{results}</Results>'
        f'<ResultSummary><Counters executed="{executed}" passed="{passed}" />'
        '</ResultSummary></TestRun>'
    )


def test_gate_accepts_real_executed_and_passing_tests(tmp_path):
    report = tmp_path / "results.trx"
    report.write_text(_trx("3", "3"), encoding="utf-8")

    result = _run_gate(report)

    assert result.returncode == 0
    assert "3 executed, 3 passed" in result.stdout


@pytest.mark.parametrize(
    "contents",
    [
        _trx("0", "0"),  # No tests discovered or executed.
        _trx("1", "0"),  # Nothing passed, even if a test was executed.
        _trx("not-a-number", "1"),
        "<TestRun />",  # Missing counters.
        "<not-valid-xml",
    ],
    ids=["zero-tests", "zero-passed", "bad-count", "no-counters", "invalid-xml"],
)
def test_gate_rejects_empty_or_invalid_reports(tmp_path, contents):
    report = tmp_path / "results.trx"
    report.write_text(contents, encoding="utf-8")

    result = _run_gate(report)

    assert result.returncode != 0
    assert result.stderr


def test_gate_rejects_missing_report(tmp_path):
    result = _run_gate(tmp_path / "missing.trx")

    assert result.returncode != 0
    assert "Cannot verify" in result.stderr


def test_gate_requires_the_named_test_to_pass(tmp_path):
    report = tmp_path / "results.trx"
    report.write_text(
        _trx("2", "2", '<UnitTestResult testName="Example.SafetyTest" outcome="Passed" />'),
        encoding="utf-8",
    )

    result = _run_gate(report, "--require-test", "SafetyTest")

    assert result.returncode == 0
    assert "1 required tests passed" in result.stdout


@pytest.mark.parametrize("outcome", ["Failed", "NotExecuted", "Skipped"])
def test_gate_rejects_a_required_test_that_did_not_pass(tmp_path, outcome):
    report = tmp_path / "results.trx"
    report.write_text(
        _trx("2", "1", f'<UnitTestResult testName="Example.SafetyTest" outcome="{outcome}" />'),
        encoding="utf-8",
    )

    result = _run_gate(report, "--require-test", "SafetyTest")

    assert result.returncode != 0
    assert "Required .NET test did not pass" in result.stderr


def test_gate_rejects_a_required_test_not_in_the_report(tmp_path):
    report = tmp_path / "results.trx"
    report.write_text(_trx("1", "1"), encoding="utf-8")

    result = _run_gate(report, "--require-test", "SafetyTest")

    assert result.returncode != 0
    assert "Required .NET test did not pass" in result.stderr
