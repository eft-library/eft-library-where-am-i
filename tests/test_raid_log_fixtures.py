import contextlib
from datetime import datetime
import importlib.util
import io
import json
from pathlib import Path
import re
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('replay', ROOT / 'tools/replay_raid_logs.py')
replay = importlib.util.module_from_spec(spec)
spec.loader.exec_module(replay)


class RaidLogFixturesTests(unittest.TestCase):
    def test_fixture_evidence_matches_expected_raids(self):
        expected = json.loads((replay.FIXTURES / 'expected.json').read_text())
        aliases = {'bigmap': 'customs', 'woods': 'woods', 'shoreline': 'shoreline'}
        for scenario, raids in expected.items():
            with self.subTest(scenario=scenario):
                observed = []
                current = None
                timer = None
                for _, _, _, line in replay.read_events(replay.FIXTURES / scenario):
                    if 'scene preset path:' in line:
                        current = aliases[re.search(r'rcid:(\w+)\.scenespreset', line)[1]]
                        timer = None
                    elif 'GameTimer utcNow:' in line:
                        values = re.search(r'utcNow:(.*?) start:.*? escape:(.*?) PastTime:', line)
                        now, end = [datetime.strptime(v, '%m/%d/%Y %H:%M:%S') for v in values.groups()]
                        timer = int((end - now).total_seconds())
                    elif '|GameStarted:' in line:
                        observed.append({'map': current, 'remaining_seconds': timer})
                    else:
                        self.fail('Unexpected fixture content (only map/start/timer lines allowed)')
                self.assertEqual(observed, raids)

    def test_replay_preserves_every_line_and_does_not_overwrite(self):
        events = replay.read_events(replay.FIXTURES / 'customs-woods')
        self.assertEqual(len(events), 6)
        self.assertEqual([e[0] for e in events], sorted(e[0] for e in events))
        with tempfile.TemporaryDirectory() as directory:
            destination = Path(directory) / 'session'
            with contextlib.redirect_stdout(io.StringIO()):
                replay.replay(events, destination)
            for name in ('application_000.log', 'output_000.log'):
                self.assertEqual((destination / name).read_text(),
                                 (replay.FIXTURES / 'customs-woods' / name).read_text())
            with self.assertRaises(FileExistsError):
                replay.replay(events, destination)

    def test_timing_uses_inter_event_delay_and_flushes_each_line(self):
        from unittest.mock import patch
        events = replay.read_events(replay.FIXTURES / 'customs-woods')
        with tempfile.TemporaryDirectory() as directory:
            destination = Path(directory) / 'session'
            snapshots = []
            def capture(_):
                snapshots.append(sum(len(p.read_text().splitlines()) for p in destination.iterdir()))
            with patch.object(replay.time, 'sleep', side_effect=capture) as sleep:
                with contextlib.redirect_stdout(io.StringIO()):
                    replay.replay(events, destination, speed=60)
            self.assertEqual(snapshots, [1, 2, 3, 4, 5])
            self.assertAlmostEqual(sleep.call_args_list[0].args[0],
                                   (events[1][0] - events[0][0]).total_seconds() / 60)


if __name__ == '__main__':
    unittest.main()
