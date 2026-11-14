"""Parse upstream Hookshot's progress messages for the final GUI summary.

Only report figures actually observed in the native build output. Missing counters
appear as 'Not reported' instead of invented zeroes.
"""
from __future__ import annotations
from dataclasses import dataclass
import re


@dataclass
class BuildReport:
    functions: int | None = None
    instructions: int | None = None
    lifted_files: int | None = None
    compiled_files: int | None = None
    changed_files: int | None = None
    rebuilt_files: int | None = None
    native_compile_seconds: int | None = None
    total_build_seconds: int | None = None
    renderer_linked: bool = False
    renderer_path: str = ''
    extraction_reused: bool = False
    shader_preparation: str = 'Not reported'
    pipeline_count: int | None = None
    finish_reported: bool = False
    error: str = ''

    def absorb(self, line: str):
        m = re.search(r'discovered\s+([\d,]+)\s+functions', line)
        if m:
            self.functions = int(m.group(1).replace(',', ''))
        m = re.search(r'translated instructions:\s*([\d.]+)%\s+of\s+([\d,]+)', line)
        if m:
            self.instructions = int(m.group(2).replace(',', ''))
        m = re.search(r'wrote\s+(\d+)\s+code files', line)
        if m:
            self.lifted_files = int(m.group(1))
        m = re.match(r'\s*(\d+) files,\s+\d+ jobs', line)
        if m:
            self.compiled_files = int(m.group(1))
        m = re.match(r'@@plan\s+(\d+)\s+(\d+)', line)
        if m:
            self.changed_files = int(m.group(1))
            self.compiled_files = int(m.group(2))
        if re.match(r'@@file\s+\S+\s+\S+\s+(new|again)$', line):
            self.rebuilt_files = (self.rebuilt_files or 0) + 1
        m = re.search(r'compiled in\s+(\d+)s,\s+(\d+) failed', line)
        if m:
            self.native_compile_seconds = int(m.group(1))
        m = re.search(r'the build took\s+(?:(\d+)h\s*)?(?:(\d+)m\s*)?(?:(\d+)s)?', line)
        if m and any(x for x in m.groups()):
            h, mn, sec = [int(x or 0) for x in m.groups()]
            self.total_build_seconds = 3600*h + 60*mn + sec
        m = re.search(r'linked\s+(\S*hookshot_vk\.(?:so|dll))\s*:', line)
        if m:
            self.renderer_linked = True
            self.renderer_path = m.group(1)
        if 'reading it again is skipped' in line:
            self.extraction_reused = True
        m = re.search(r'(?:shipped|recorded) pipeline state list:\s*(\d+) pipelines', line)
        if m:
            self.pipeline_count = int(m.group(1))
        if line.startswith('the shaders were not prepared now'):
            self.shader_preparation = 'Skipped / failed — ' + line
        elif ('shader' in line.lower() or 'pipeline' in line.lower()) and ('prepared' in line.lower() or 'preparation complete' in line.lower()):
            if self.shader_preparation == 'Not reported':
                self.shader_preparation = 'Preparation reported by the native builder'
        if line.startswith('@@done '):
            self.finish_reported = True
        if line.startswith('@@error '):
            self.error = line[8:]


def display(value, suffix=''):
    return f'{value:,}{suffix}' if value is not None else 'Not reported'
