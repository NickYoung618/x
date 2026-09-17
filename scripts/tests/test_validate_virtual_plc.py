"""Verify that the integration gate cannot report partial core tests as PASS."""
import importlib.util
from pathlib import Path
import tempfile
import unittest


SCRIPT = Path(__file__).resolve().parents[1] / "validate-virtual-plc.py"
SPEC = importlib.util.spec_from_file_location("validate_virtual_plc", SCRIPT)
assert SPEC and SPEC.loader
validation = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(validation)


def trx(assembly, total=1, passed=1):
    return (
        '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">'
        f'<TestDefinitions><UnitTest><TestMethod codeBase="/build/{assembly}.dll" /></UnitTest></TestDefinitions>'
        f'<ResultSummary><Counters total="{total}" passed="{passed}" /></ResultSummary>'
        '</TestRun>'
    )


class ReportGateTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.directory = Path(self.temp.name)

    def write(self, name, content):
        (self.directory / name).write_text(content, encoding="utf-8")

    def test_accepts_exact_current_assemblies(self):
        expected = validation.test_projects()
        for index, assembly in enumerate(sorted(expected)):
            self.write(f"{index}.trx", trx(assembly))
        results = validation.verify_test_reports(self.directory, expected)
        self.assertEqual(set(results), expected)

    def test_missing_assembly_fails(self):
        self.write("one.trx", trx("Inspection.Domain.Tests"))
        with self.assertRaisesRegex(RuntimeError, "Expected .* TRX reports"):
            validation.verify_test_reports(self.directory, validation.test_projects())

    def test_duplicate_assembly_fails_even_with_expected_report_count(self):
        expected = validation.test_projects()
        names = sorted(expected)
        for index, assembly in enumerate(names):
            self.write(f"{index}.trx", trx(names[0] if index == 1 else assembly))
        with self.assertRaisesRegex(RuntimeError, "Duplicate test project report"):
            validation.verify_test_reports(self.directory, expected)

    def test_skipped_or_failed_test_fails(self):
        self.write("one.trx", trx("Inspection.Domain.Tests", total=2, passed=1))
        with self.assertRaisesRegex(RuntimeError, "failed or were skipped"):
            validation.verify_test_reports(self.directory, {"Inspection.Domain.Tests"})


if __name__ == "__main__":
    unittest.main()
