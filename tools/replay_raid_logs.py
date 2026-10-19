#!/usr/bin/env python3
"""Append sanitized fixture lines in timestamp order; never contact the server."""
import argparse
from datetime import datetime
from pathlib import Path
import tempfile
import time

FIXTURES = Path(__file__).resolve().parents[1] / 'tests/fixtures/raid-logs'


def read_events(scenario):
    events = []
    for name in ('application_000.log', 'output_000.log'):
        for line_number, line in enumerate((scenario / name).read_text().splitlines(), 1):
            timestamp = datetime.strptime(line[:23], '%Y-%m-%d %H:%M:%S.%f')
            events.append((timestamp, name, line_number, line))
    return sorted(events)


def replay(events, destination, speed=0):
    if speed < 0:
        raise ValueError('speed must be nonnegative')
    # A fresh directory prevents accidental truncation or mixing with old sessions.
    destination.mkdir(parents=True, exist_ok=False)
    for name in ('application_000.log', 'output_000.log'):
        (destination / name).touch()
    previous = None
    for timestamp, name, _, line in events:
        if speed and previous is not None:
            time.sleep((timestamp - previous).total_seconds() / speed)
        with (destination / name).open('a', encoding='utf-8') as stream:
            stream.write(line + '\n')
        print(f'{timestamp.isoformat(timespec="milliseconds")} {name}', flush=True)
        previous = timestamp


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    scenarios = sorted(p.name for p in FIXTURES.iterdir() if p.is_dir())
    parser.add_argument('scenario', choices=scenarios)
    parser.add_argument('--destination', type=Path, help='New directory; existing paths are rejected')
    parser.add_argument('--speed', type=float, default=0,
                        help='0: immediate, 1: original timing, 60: 60x faster')
    args = parser.parse_args()
    if not 0 <= args.speed < float('inf'):
        parser.error('--speed must be finite and nonnegative')
    destination = args.destination or Path(tempfile.mkdtemp(prefix='eft-replay-')) / 'session'
    events = read_events(FIXTURES / args.scenario)
    print(f'Destination: {destination.resolve()}', flush=True)
    replay(events, destination, args.speed)


if __name__ == '__main__':
    main()
