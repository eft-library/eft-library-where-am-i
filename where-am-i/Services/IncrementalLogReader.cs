using System.IO;
using System.Text;

namespace where_am_i.Services
{
    // Byte offsets preserve split UTF-8 characters and lines across polls.
    internal sealed class IncrementalLogReader
    {
        private const int MaxLineBytes = 64 * 1024;
        private long offset;
        private byte[] checkpoint = [];
        private byte[] pending = [];
        private bool discardingLine;
        private DateTime lastWrite;
        private DateTime creation;

        public async Task<(bool Reset, IReadOnlyList<string> Lines)> ReadAsync(string path)
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 8192, FileOptions.Asynchronous);
            var info = new FileInfo(path);
            var reset = stream.Length < offset || (offset > 0 && info.CreationTimeUtc != creation) ||
                (offset > 0 && stream.Length == offset && info.LastWriteTimeUtc != lastWrite);
            if (!reset && checkpoint.Length > 0)
            {
                stream.Position = offset - checkpoint.Length;
                var actual = new byte[checkpoint.Length];
                await stream.ReadExactlyAsync(actual);
                reset = !actual.AsSpan().SequenceEqual(checkpoint);
            }

            var cursor = reset ? 0 : offset;
            stream.Position = cursor;
            using var line = new MemoryStream();
            if (!reset) line.Write(pending);
            var discard = !reset && discardingLine;
            var lines = new List<string>();
            var buffer = new byte[8192];
            // Snapshot length: a continuously writing game cannot keep this poll open forever.
            var length = stream.Length;
            while (cursor < length)
            {
                var count = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, length - cursor)));
                if (count == 0) throw new IOException("로그 파일이 읽는 중 변경되었습니다.");
                cursor += count;
                for (var i = 0; i < count; i++)
                {
                    var value = buffer[i];
                    if (value == (byte)'\n')
                    {
                        if (!discard)
                        {
                            var text = Encoding.UTF8.GetString(line.GetBuffer(), 0, (int)line.Length).TrimEnd('\r').TrimStart('\uFEFF');
                            if (text.Contains("|application|scene preset path:", StringComparison.Ordinal) ||
                                text.Contains("|application|GameStarted:", StringComparison.Ordinal) ||
                                text.Contains("|output|GameTimer utcNow:", StringComparison.Ordinal))
                                lines.Add(text);
                        }
                        line.SetLength(0);
                        discard = false;
                    }
                    else if (!discard)
                    {
                        if (line.Length == MaxLineBytes)
                        {
                            line.SetLength(0);
                            discard = true;
                        }
                        else line.WriteByte(value);
                    }
                }
            }
            var nextCheckpoint = new byte[(int)Math.Min(64, cursor)];
            stream.Position = cursor - nextCheckpoint.Length;
            await stream.ReadExactlyAsync(nextCheckpoint);
            // Commit only after successful I/O; failures leave the old cursor retryable.
            offset = cursor;
            checkpoint = nextCheckpoint;
            pending = line.ToArray();
            discardingLine = discard;
            lastWrite = info.LastWriteTimeUtc;
            creation = info.CreationTimeUtc;
            return (reset, lines);
        }
    }
}
