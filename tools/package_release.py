#!/usr/bin/env python3
"""Validate and package a local Windows x64 release. Does not upload or run the app."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import zipfile
from datetime import datetime, timezone

ROOT = Path(__file__).resolve().parents[1]


def run(args):
    subprocess.run(args, cwd=ROOT, check=True, env={**os.environ, 'DOTNET_CLI_TELEMETRY_OPTOUT': '1'})


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dotnet', default='dotnet', help='Path to .NET 10 SDK executable')
    parser.add_argument('--version', default='4.0.0', help='2-4 numeric components')
    args = parser.parse_args()
    if not re.fullmatch(r'\d+(?:\.\d+){1,3}', args.version) or any(int(n) > 65534 for n in args.version.split('.')):
        parser.error('version must contain 2-4 numeric components between 0 and 65534')
    run([args.dotnet, 'run', '--project', 'tests/RaidSessionChecks/RaidSessionChecks.csproj', '-c', 'Release'])
    run([sys.executable, '-m', 'unittest', 'discover', '-s', 'tests', '-p', 'test_*.py', '-v'])
    artifacts = ROOT / 'artifacts'
    artifacts.mkdir(exist_ok=True)
    name = f'where-am-i-{args.version}-win-x64'
    with tempfile.TemporaryDirectory(prefix='.package-', dir=artifacts) as temporary:
        staging = Path(temporary)
        payload = staging / name
        payload.mkdir()
        run([args.dotnet, 'publish', 'where-am-i/where-am-i.csproj', '-c', 'Release', '-r', 'win-x64',
             '--self-contained', 'true', '-p:EnableWindowsTargeting=true', '-p:PublishSingleFile=true',
             '-p:PublishReadyToRun=false', f'-p:Version={args.version}', '-o', str(payload), '--nologo'])
        exe = payload / 'where-am-i.exe'
        if not exe.is_file() or exe.read_bytes()[:2] != b'MZ':
            raise RuntimeError('Expected Windows executable missing')
        if any(p.suffix.lower() in {'.pdb', '.log'} for p in payload.rglob('*') if p.is_file()):
            raise RuntimeError('Unexpected debug or log file in publish output')
        (payload / 'docs').mkdir()
        for document in ('USER_GUIDE.md', 'RELEASE_NOTES.md', 'WINDOWS_VALIDATION.md'):
            shutil.copy2(ROOT / 'docs' / document, payload / 'docs' / document)
        shutil.copytree(ROOT / 'docs/images', payload / 'docs/images')
        (payload / 'README.txt').write_text(
            f'EFT Library Where Am I {args.version} / Windows x64\n'
            'Extract the ZIP, then run where-am-i.exe. No separate .NET installation is required.\n'
            'User guide: docs/USER_GUIDE.md\nRelease notes: docs/RELEASE_NOTES.md\n'
            'Windows gameplay and backend/web integration remain to be verified: docs/WINDOWS_VALIDATION.md\n'
            'This local package has not been signed, uploaded, or scanned by this packaging script.\n', encoding='utf-8')
        manifest = {
            'product': 'where-am-i', 'version': args.version, 'runtime': 'win-x64',
            'created_at': datetime.now(timezone.utc).isoformat(),
            'configuration': 'Release', 'self_contained': True, 'single_file': True,
            'ready_to_run': False, 'windows_runtime_verified': False,
            'backend_web_integration_verified': False,
            'files': {str(p.relative_to(payload)).replace(os.sep, '/'): hashlib.sha256(p.read_bytes()).hexdigest()
                      for p in sorted(payload.rglob('*')) if p.is_file()}}
        (payload / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
        archive = artifacts / (name + '.zip')
        staged_zip = staging / archive.name
        with zipfile.ZipFile(staged_zip, 'w', compression=zipfile.ZIP_DEFLATED) as output:
            for path in sorted(payload.rglob('*')):
                if path.is_file():
                    output.write(path, path.relative_to(staging))
        with zipfile.ZipFile(staged_zip) as output:
            if output.testzip() is not None:
                raise RuntimeError('ZIP integrity check failed')
        staged_zip.replace(archive)
        digest = hashlib.sha256(archive.read_bytes()).hexdigest()
        archive.with_suffix('.zip.sha256').write_text(f'{digest}  {archive.name}\n', encoding='utf-8')
        (artifacts / (name + '.manifest.json')).write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
        print(f'Package: {archive}\nSHA256: {digest}', flush=True)


if __name__ == '__main__':
    main()
