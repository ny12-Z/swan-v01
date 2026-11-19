using System;
using System.Collections.Generic;

namespace Swan.Core.Data
{
    /// <summary>新手教程的一步。标题/正文都在表里，HUD 只负责显示。</summary>
    public sealed class TutorialStep
    {
        public int Step;
        public string Title;
        public string Text;
        public int SourceLine;
        public string FileName;

        public override string ToString()
        {
            return $"{Step}:{Title}";
        }
    }

    public sealed class TutorialTable
    {
        public const string FileName = "教程.csv";

        readonly List<TutorialStep> _steps;

        public IReadOnlyList<TutorialStep> Steps => _steps;

        TutorialTable(List<TutorialStep> steps)
        {
            _steps = steps;
        }

        public TutorialStep Get(int index)
        {
            if (_steps.Count == 0) return null;
            if (index < 0) index = 0;
            if (index >= _steps.Count) index = _steps.Count - 1;
            return _steps[index];
        }

        public static TutorialTable Load(CsvDocument doc, DataWarnings warnings)
        {
            var steps = new List<TutorialStep>();
            var seen = new HashSet<int>();
            foreach (var row in doc.Rows)
            {
                int step = row.Int("step");
                if (!seen.Add(step)) throw row.Error("step", $"序号重复：{step}");
                steps.Add(new TutorialStep
                {
                    Step = step,
                    Title = row.Require("title"),
                    Text = row.Require("text"),
                    SourceLine = row.Line,
                    FileName = doc.FileName
                });
            }

            steps.Sort((a, b) => a.Step.CompareTo(b.Step));
            if (steps.Count == 0) throw new DataLoadException(doc.FileName, 1, null, "教程表是空的");
            return new TutorialTable(steps);
        }
    }
}
