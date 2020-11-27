using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Perftop.PerfDataTypes;

namespace Perftop
{
    class Program
    {
        public const ulong MagicNumber = 0x32454c4946524550;

        private static ConcurrentQueue<string> _callstacks = new ConcurrentQueue<string>();

        // ReSharper disable once NotAccessedField.Local
        private static Timer? _timer;

        static void Main()
        {
            var knownPids = new HashSet<uint>();

            _timer = new Timer(_ => ProcessCallstacks(), null, 3000, 3000);

            var symbols = new SortedDictionary<ulong, string>();

            using var input = Console.OpenStandardInput();

            var fileHeader = input.Read<PerfPipeFileHeader>();

            if (fileHeader.Magic != MagicNumber)
            {
                Console.WriteLine($"Magic number mismatch. Expected {MagicNumber:x2}, found {fileHeader.Magic:x2}");
                return;
            }

            bool endOfFile = false;

            PerfRecordIndexes? indexes = null;

            while (!endOfFile)
            {
                var header = input.Read<PerfEventHeader>();

                switch (header.Type)
                {
                    case PerfEventType.EndOfFile:
                        endOfFile = true;
                        break;

                    case PerfEventType.RecordMmap:
                        {
                            var perfRecordMmap = new PerfRecordMmap(input, header);

                            var filename = ReadString(perfRecordMmap.Filename);

                            symbols.Add(perfRecordMmap.Addr, filename);

                            ReadSymbols(filename, perfRecordMmap.Addr, perfRecordMmap.Pgoff, perfRecordMmap.Len, symbols);

                            if (knownPids.Add(perfRecordMmap.Pid))
                            {
                                ReadPerfMap(perfRecordMmap.Pid, symbols);
                            }

                            break;
                        }

                    case PerfEventType.LostEvents:
                        Console.WriteLine("Lost events");
                        input.Skip(header.GetRemainingBytes());
                        break;

                    case PerfEventType.RecordComm:
                        //var perfRecordComm = new PerfRecordComm(input, header);
                        input.Skip(header.GetRemainingBytes());
                        break;

                    case PerfEventType.RecordExit:
                        input.Skip(header.GetRemainingBytes());
                        break;

                    case PerfEventType.RecordFork:
                        //var perfRecordFork = new PerfRecordFork(input, header);
                        input.Skip(header.GetRemainingBytes());
                        break;

                    case PerfEventType.RecordSample:
                        if (indexes == null)
                        {
                            break;
                        }

                        var sample = new PerfRecordSample(input, header, indexes);

                        var symbol = "UNKNOWN";

                        foreach (var kvp in symbols)
                        {
                            if (kvp.Key < sample.Addr)
                            {
                                symbol = kvp.Value;
                            }
                            else
                            {
                                break;
                            }
                        }

                        _callstacks.Enqueue(symbol);

                        break;

                    case PerfEventType.RecordMmap2:
                        {
                            var perfRecordMmap2 = new PerfRecordMmap2(input, header);

                            var filename = ReadString(perfRecordMmap2.filename);

                            symbols.Add(perfRecordMmap2.Addr, filename);

                            ReadSymbols(filename, perfRecordMmap2.Addr, perfRecordMmap2.Pgoff, perfRecordMmap2.Len, symbols);

                            if (knownPids.Add(perfRecordMmap2.Pid))
                            {
                                ReadPerfMap(perfRecordMmap2.Pid, symbols);
                            }

                            break;
                        }

                    case PerfEventType.RecordKSymbol:
                        //var perfRecordKSymbol = new PerfRecordKSymbol(input, header);
                        input.Skip(header.GetRemainingBytes());
                        break;

                    case PerfEventType.RecordBpfEvent:
                        //var perfRecordBpfEvent = new PerfRecordBpfEvent(input, header);
                        input.Skip(header.GetRemainingBytes());
                        break;

                    case PerfEventType.RecordHeaderAttr:
                        var perfRecordHeaderAttr = new PerfRecordHeaderAttr(input, header);
                        indexes = new PerfRecordIndexes(perfRecordHeaderAttr.Attr.SampleType);

                        break;

                    case PerfEventType.RecordFinishedRound:
                        input.Skip(header.GetRemainingBytes());
                        break;

                    case PerfEventType.RecordThreadMap:
                        //var perfRecordThreadMap = new PerfRecordThreadMap(input, header);
                        input.Skip(header.GetRemainingBytes());
                        break;

                    case PerfEventType.RecordCpuMap:
                        //var perfRecordCpuMap = new PerfRecordCpuMap(input, header);
                        input.Skip(header.GetRemainingBytes());
                        break;

                    case PerfEventType.RecordEventUpdate:
                        //var perfRecordEventUpdate = new PerfRecordEventUpdate(input, header);
                        input.Skip(header.GetRemainingBytes());
                        break;

                    case PerfEventType.RecordTimeConv:
                        //var perfRecordTimeConv = new PerfRecordTimeConv(input, header);
                        input.Skip(header.GetRemainingBytes());
                        break;

                    case PerfEventType.RecordHeaderFeature:
                        //var perfRecordHeaderFeature = new PerfRecordHeaderFeature(input, header);
                        input.Skip(header.GetRemainingBytes());
                        break;

                    default:
                        throw new NotSupportedException("Header not supported: " + header.Type);
                }
            }
        }

        private static void ReadPerfMap(uint pid, SortedDictionary<ulong, string> symbols)
        {
            var perfMapFile = $"/tmp/perf-{pid}.map";

            if (File.Exists(perfMapFile))
            {
                foreach (var line in File.ReadLines(perfMapFile))
                {
                    var values = line.Split(' ', 3);

                    symbols.Add(Convert.ToUInt64(values.First(), 16), values.Last());
                }
            }
        }

        private static void ProcessCallstacks()
        {
            var callstacks = Interlocked.Exchange(ref _callstacks, new ConcurrentQueue<string>());

            var groups = callstacks.GroupBy(c => c).OrderByDescending(g => g.Count()).ToList();

            double sum = groups.Sum(g => g.Count());

            Console.Clear();

            Console.WriteLine("    .NET PerfTop");

            Console.WriteLine();

            Console.WriteLine(new string('-', Console.WindowWidth - 5));

            Console.WriteLine();

            foreach (var element in groups.Take(50))
            {
                var percentage = Math.Round(element.Count() * 100 / sum, 2);

                var oldColor = Console.ForegroundColor;

                if (percentage >= 5)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                }
                else if (percentage >= 0.5)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                }

                var stringPercentage = $"{percentage} %";

                Console.Write(stringPercentage);

                Console.ForegroundColor = oldColor;

                Console.WriteLine(string.Concat($"{stringPercentage}\t{element.Key}".Skip(stringPercentage.Length).Take(Console.WindowWidth - 8)));
            }
        }

        private static string ReadString(byte[] data)
        {
            int length;

            for (length = 0; length < data.Length; length++)
            {
                if (data[length] == 0x0)
                {
                    break;
                }
            }

            return Encoding.ASCII.GetString(data.AsSpan(0, length));
        }

        private static void ReadSymbols(string filename, ulong baseAddress, ulong offset, ulong length, SortedDictionary<ulong, string> symbols)
        {
            if (File.Exists(filename + ".dbg"))
            {
                filename += ".dbg";
            }
            else if (!File.Exists(filename))
            {
                return;
            }

            bool dynamicSymbols = false;

        readFile:

            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "nm",
                Arguments = $"-C {(dynamicSymbols ? "-D" : "")} --defined-only -a -v {filename}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process == null)
            {
                throw new InvalidOperationException("Failed to invoke nm");
            }

            int count = 0;

            while (!process.StandardOutput.EndOfStream)
            {
                var line = process.StandardOutput.ReadLine();
                var values = line!.Split(' ', 3);

                if (values[1] == "U")
                {
                    continue;
                }

                try
                {
                    if (values[0].Trim().Length == 0)
                    {
                        continue;
                    }

                    var addr = Convert.ToUInt64(values[0], 16);

                    if (addr >= offset && addr < (offset + length))
                    {
                        var destAddr = baseAddress + addr - offset;
                        symbols[destAddr] = values[2];

                        count++;
                    }
                }
                catch (FormatException)
                {
                }
            }

            process.Dispose();

            if (count == 0 && !dynamicSymbols)
            {
                dynamicSymbols = true;
                goto readFile;
            }
        }
    }
}