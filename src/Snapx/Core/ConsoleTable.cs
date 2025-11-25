/*

The MIT License (MIT)

Copyright (c) 2012 Khalid Abuhakmeh

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

*/

using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using Snap.Logging;

namespace snapx.Core;

internal sealed class ConsoleTable
{
    public string Header { get; set; }
    [UsedImplicitly] public IList<object> Columns { get; }
    [UsedImplicitly] public IList<object[]> Rows { get; }
    [UsedImplicitly] public ConsoleTableOptions Options { get; }

    public ConsoleTable([NotNull] params string[] columns)
        :this(columns.ToList())
    {
    }

    public ConsoleTable([NotNull] IEnumerable<string> columns)
        : this(columns.ToList())
    {

    }

    public ConsoleTable([NotNull] List<string> columns)
        : this(new ConsoleTableOptions { Columns = new List<string>(columns) })
    {
        if (columns == null) throw new ArgumentNullException(nameof(columns));
        if (columns.Count == 0) throw new ArgumentException("Value cannot be an empty collection.", nameof(columns));
    }

    public ConsoleTable([NotNull] ConsoleTableOptions options)
    {
        if (options == null) throw new ArgumentNullException(nameof(options));
        Options = options ?? throw new ArgumentNullException(nameof(options));
        Rows = new List<object[]>();
        Columns = new List<object>(options.Columns);
    }

    // ReSharper disable once UnusedMethodReturnValue.Global
    public ConsoleTable AddRow([NotNull] object[] values)
    {
        if (values == null) throw new ArgumentNullException(nameof(values));

        if (!Columns.Any())
        {
            throw new Exception("Please set the columns first");
        }

        if (Columns.Count != values.Length)
        {
            throw new Exception(
                $"The number columns in the row ({Columns.Count}) does not match the values ({values.Length}");
        }

        Rows.Add(values.ToArray());
        return this;
    }

    List<int> ColumnLengths()
    {
        var columnLengths = Columns
            .Select((_, i) => Rows.Select(x => x[i])
                .Union([Columns[i]])
                .Where(x => x != null)
                .Select(x =>
                {
                    var s = x.ToString();
                    return s?.Length ?? 0;
                }).Max())
            .ToList();
        return columnLengths;
    }

    public void Write([NotNull] ILog logger)
    {
        if (logger == null) throw new ArgumentNullException(nameof(logger));

        // find the longest column by searching each row
        var columnLengths = ColumnLengths();

        // create the string format with padding
        var format = Enumerable.Range(0, Columns.Count)
            .Select(i => "| {" + i + ",-" + columnLengths[i] + "} ")
            .Aggregate((s, a) => s + a) + " |";

        // remove last pipe (|)
        format = format[0..^1];

        // find the longest formatted line
        var maxRowLength = Math.Max(0, Rows.Any() ? Rows.Max(row => string.Format(format, row).Length) : 0);
        var columnHeaders = string.Format(format, Columns.ToArray());

        // longest line is greater of formatted columnHeader and longest row
        var longestLine = Math.Min(Program.TerminalBufferWidth, Math.Max(maxRowLength, columnHeaders.Length));

        // add each row
        var results = Rows.Select(row => string.Format(format, row)).ToList();

        // create the divider
        var divider = $"{string.Join(string.Empty, Enumerable.Repeat("-", longestLine))} ";

        if (Header != null)
        {
            var dividerHeader = string.Join(string.Empty, Enumerable.Repeat("=", longestLine));
            foreach (var line in Header.Split("\n"))
            {
                logger.Info(line);
            }
            logger.Info(dividerHeader);
        }

        logger.Info(columnHeaders);

        foreach (var row in results)
        {
            logger.Info(divider);
            logger.Info(row);
        }

        if (!Options.EnableCount) return;
            
        logger.Info(string.Empty);
        logger.Info(" Count: {0}", Rows.Count);
    }
}

public class ConsoleTableOptions
{
    public IEnumerable<string> Columns { get; init; } = new List<string>();
    public bool EnableCount { get; [UsedImplicitly] set; } = false;
}