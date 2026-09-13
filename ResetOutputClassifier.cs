using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Windows.Media;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;

namespace EnCReset
{
    [Export(typeof(IClassifierProvider))]
    [ContentType("output")]
    internal sealed class ResetOutputClassifierProvider : IClassifierProvider
    {
        [Export(typeof(ClassificationTypeDefinition))]
        [Name(ResetOutputClassifier.ClassificationName)]
        internal static ClassificationTypeDefinition SuccessType = null;

        [Import]
        internal IClassificationTypeRegistryService Registry = null;

        public IClassifier GetClassifier(ITextBuffer buffer)
        {
            return buffer.Properties.GetOrCreateSingletonProperty(
                () => new ResetOutputClassifier(Registry.GetClassificationType(ResetOutputClassifier.ClassificationName)));
        }
    }

    internal sealed class ResetOutputClassifier : IClassifier
    {
        internal const string ClassificationName = "EnC Reset Success";
        internal const string SuccessMessage = "[EnC Reset] Successfully reset stale Edit & Continue tracking.";
        readonly IClassificationType _classification;

        internal ResetOutputClassifier(IClassificationType classification)
        {
            _classification = classification;
        }

        // Classification depends only on the requested snapshot's text.
        public event EventHandler<ClassificationChangedEventArgs> ClassificationChanged
        {
            add { }
            remove { }
        }

        public IList<ClassificationSpan> GetClassificationSpans(SnapshotSpan span)
        {
            var result = new List<ClassificationSpan>();
            if (span.IsEmpty)
                return result;

            var first = span.Start.GetContainingLine().LineNumber;
            var last = span.Snapshot.GetLineFromPosition(span.End.Position - 1).LineNumber;
            for (var number = first; number <= last; number++)
            {
                var line = span.Snapshot.GetLineFromLineNumber(number);
                if (!string.Equals(line.GetText(), SuccessMessage, StringComparison.Ordinal))
                    continue;

                var intersection = line.Extent.Intersection(span);
                if (intersection.HasValue && !intersection.Value.IsEmpty)
                    result.Add(new ClassificationSpan(intersection.Value, _classification));
            }
            return result;
        }
    }

    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = ResetOutputClassifier.ClassificationName)]
    [Name(ResetOutputClassifier.ClassificationName)]
    [UserVisible(true)]
    [Order(After = Priority.High)]
    internal sealed class ResetSuccessFormat : ClassificationFormatDefinition
    {
        public ResetSuccessFormat()
        {
            DisplayName = "EnC Reset Success";
            ForegroundColor = Color.FromRgb(0x30, 0xA4, 0x64);
            IsBold = true;
        }
    }
}