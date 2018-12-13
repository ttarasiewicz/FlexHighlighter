﻿using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Language.StandardClassification;
using System.Linq;
using System.Diagnostics;

namespace Flex_Highlighter
{
    public class MultiLineToken
    {
        public IClassificationType Classification;
        public ITrackingSpan Tracking;
        public ITextVersion Version;
        public Languages Language;
    }
    public enum Cases
    {
        NoCase = 0,
        MultiLineComment,
        Option,
        Include,
        C,
        CIndent,
        CEnding,
        FlexDefinitions,
        CMacro,
        FlexRules,
        DefinitionUsed
    }
    public class Token
    {
        public int StartIndex;
        public int Length;
        public int TokenId;
        public int State;
    }

    public enum Languages
    {
        Flex,
        C,
        FlexDefinitions,
        NoLanguage,
        CEnding
    }
    /// <summary>
    /// Classifier that classifies all text as an instance of the "FlexerClassifier" classification type.
    /// </summary>
    internal sealed class FlexClassifier : IClassifier
    {
        /// <summary>
        /// Classification type.
        /// </summary>
        ///
        internal List<MultiLineToken> _multiLineTokens;
        private readonly IClassificationType classificationType;
        internal readonly IClassificationType FlexDefinitionType;
        internal readonly IClassificationType FlexDefinitionSection;
        internal readonly IClassificationType FlexSection;
        internal readonly IClassificationType CSection;
        /// <summary>
        /// Initializes a new instance of the <see cref="FlexClassifier"/> class.
        /// </summary>
        /// <param name="registry">Classification registry.</param>
        internal FlexClassifier(ITextBuffer buffer, IStandardClassificationService classification, IClassificationTypeRegistryService registry)
        {
            ClassificationRegistry = registry;
            Classification = classification;
            Buffer = buffer;
            FlexDefinitionType = registry.CreateClassificationType("Flex Definition", new IClassificationType[0]);
            CSection = registry.CreateClassificationType("CSection", new IClassificationType[0]);
            FlexSection = registry.CreateClassificationType("FlexSection", new IClassificationType[0]);
            FlexDefinitionSection = registry.CreateClassificationType("FlexDefinitionSection", new IClassificationType[0]);
            _multiLineTokens = new List<MultiLineToken>();

            tokenizer = new FlexTokenizer(classification);
            this.classificationType = registry.GetClassificationType("FlexerClassifier");
        }


        private readonly FlexTokenizer tokenizer;

        internal ITextBuffer Buffer { get; }
        internal IClassificationTypeRegistryService ClassificationRegistry { get; }
        internal IStandardClassificationService Classification { get; }

        #region IClassifier
        internal void Invalidate(SnapshotSpan span)
        {
            if (ClassificationChanged != null)
            {
                ClassificationChanged(this, new ClassificationChangedEventArgs(span));
            }
        }

#pragma warning disable 67

        /// <summary>
        /// An event that occurs when the classification of a span of text has changed.
        /// </summary>
        /// <remarks>
        /// This event gets raised if a non-text change would affect the classification in some way,
        /// for example typing /* would cause the classification to change in C# without directly
        /// affecting the span.
        /// </remarks>
        public event EventHandler<ClassificationChangedEventArgs> ClassificationChanged;

#pragma warning restore 67

        /// <summary>
        /// Gets all the <see cref="ClassificationSpan"/> objects that intersect with the given range of text.
        /// </summary>
        /// <remarks>
        /// This method scans the given SnapshotSpan for potential matches for this classification.
        /// In this instance, it classifies everything and returns each span as a new ClassificationSpan.
        /// </remarks>
        /// <param name="span">The span currently being classified.</param>
        /// <returns>A list of ClassificationSpans that represent spans identified to be of this classification.</returns>

        public IList<ClassificationSpan> GetClassificationSpans(SnapshotSpan span)
        {

            var list = new List<ClassificationSpan>();
            bool isInsideMultiline = false;
            Languages language = Languages.FlexDefinitions;
            Languages auxLanguage = Languages.FlexDefinitions;
            Cases ecase = Cases.NoCase;
            ITextSnapshot snapshot = span.Snapshot;
            List<Tuple<Languages, int>> sectionDistances = new List<Tuple<Languages, int>>();
            List<int[]> innerSections = new List<int[]>();
            foreach (var token in _multiLineTokens.Where(x => x.Classification != null).ToList())
            {
                var auxSpan = token.Tracking.GetSpan(snapshot);
                innerSections.Add(new int[2] { auxSpan.Start.Position + 2, auxSpan.End.Position });
            }
            if (!_multiLineTokens.Where(x => x.Language == Languages.FlexDefinitions && x.Classification == null).Any())
            {
                var mlt = HandleFlexDefinitions(span, innerSections);
                if (mlt != null)
                {
                    _multiLineTokens.Add(mlt);
                    Invalidate(new SnapshotSpan(mlt.Tracking.GetStartPoint(snapshot), mlt.Tracking.GetEndPoint(snapshot).Add(mlt.Tracking.GetEndPoint(snapshot) > snapshot.Length - 2 ? 0 : 2)));
                }
            }
            _multiLineTokens = _multiLineTokens.OrderBy( x => x.Tracking.GetStartPoint(snapshot).Position).ToList();
            for (int i = _multiLineTokens.Count - 1; i >= 0; i--)
            {
                if (_multiLineTokens.Where(x => x.Tracking.GetSpan(snapshot) == _multiLineTokens[i].Tracking.GetSpan(snapshot) && x != _multiLineTokens[i]).Any())
                {
                    _multiLineTokens.RemoveAt(i);
                }
            }
            for (int i = _multiLineTokens.Count - 1; i >= 0; i--)
            {
                var multiSpan = _multiLineTokens[i].Tracking.GetSpan(span.Snapshot);
                if (multiSpan.Length == 0)
                {
                    _multiLineTokens.RemoveAt(i);
                }
                else
                {
                    if (span.IntersectsWith(multiSpan))
                    {
                        isInsideMultiline = true;
                        if (span.Snapshot.Version != _multiLineTokens[i].Version)
                        {
                            auxLanguage = _multiLineTokens[i].Language;
                            if (_multiLineTokens[i].Classification != null)
                            {
                                Invalidate(multiSpan);
                                span = new SnapshotSpan(span.Start.Position < multiSpan.Start.Position ? span.Start : multiSpan.Start, span.End.Position > multiSpan.End.Position ? span.End : multiSpan.End);
                                _multiLineTokens.RemoveAt(i);
                                continue;
                            }
                            MultiLineToken mlt = null;
                            foreach (var token in _multiLineTokens)
                            {
                                var auxSpan = token.Tracking.GetSpan(snapshot);
                                if ((auxSpan.Start.Position >= multiSpan.Start.Position && auxSpan.Start.Position < multiSpan.End && token != _multiLineTokens[i]) || token.Classification != null)
                                {
                                    innerSections.Add(new int[2] { auxSpan.Start.Position + 2, auxSpan.End.Position });
                                }
                            }
                            var spanUnion = new SnapshotSpan(new SnapshotPoint(snapshot, Math.Min(span.Start.Position, multiSpan.Start.Position)), new SnapshotPoint(snapshot, Math.Max(span.End.Position, multiSpan.End.Position)));
                            if (auxLanguage == Languages.FlexDefinitions)
                                mlt = HandleFlexDefinitions(span, innerSections);
                            else if (auxLanguage == Languages.Flex)
                                mlt = GetLanguageSpan(spanUnion, innerSections,Languages.FlexDefinitions);
                            else if (auxLanguage == Languages.CEnding)
                                mlt = GetLanguageSpan(spanUnion, innerSections, Languages.Flex);
                            else
                                mlt = GetLanguageSpan(spanUnion, innerSections);

                            if (mlt != null)
                            {
                                if (multiSpan.Start == mlt.Tracking.GetStartPoint(snapshot) && multiSpan.End == mlt.Tracking.GetEndPoint(snapshot))
                                {
                                    sectionDistances.Add(new Tuple<Languages, int>(auxLanguage, span.Start - multiSpan.Start));
                                    continue;
                                }
                                _multiLineTokens.RemoveAt(i);
                                List<MultiLineToken> auxList = new List<MultiLineToken>(_multiLineTokens.Where(x => x.Classification == null && x.Tracking.GetStartPoint(snapshot).Position >= mlt.Tracking.GetStartPoint(snapshot).Position));
                                foreach (var token in auxList)
                                {
                                    _multiLineTokens.Remove(token);
                                    Invalidate(new SnapshotSpan(token.Tracking.GetStartPoint(snapshot), token.Tracking.GetEndPoint(snapshot).Add(token.Tracking.GetEndPoint(snapshot) > snapshot.Length - 2 ? 0 : 2)));
                                }
                                ClearTokenIntersections(mlt.Tracking.GetSpan(snapshot), snapshot);
                                if (mlt.Language == Languages.Flex && _multiLineTokens.Where(x => x.Language == Languages.Flex).Any())
                                    _multiLineTokens.Remove(_multiLineTokens.Where(x => x.Language == Languages.Flex).First());
                                i = _multiLineTokens.Count();
                                _multiLineTokens.Add(mlt);
                                Invalidate(new SnapshotSpan(mlt.Tracking.GetStartPoint(snapshot), mlt.Tracking.GetEndPoint(snapshot).Add(mlt.Tracking.GetEndPoint(snapshot) > snapshot.Length - 2 ? 0 : 2)));
                                if (auxLanguage == Languages.FlexDefinitions)
                                {
                                    FlexTokenizer.Definitions.Clear();
                                    SnapshotSpan? auxSpan = _multiLineTokens.Where(x => x.Language == Languages.Flex && x.Classification == null)?.FirstOrDefault()?.Tracking?.GetSpan(snapshot);
                                    if (auxSpan != null)
                                        Invalidate((SnapshotSpan)auxSpan);
                                }
                                sectionDistances.Add(new Tuple<Languages, int>(auxLanguage, span.Start - multiSpan.Start));
                            }
                            else
                            {
                                _multiLineTokens.RemoveAt(i);
                                if (auxLanguage == Languages.Flex)
                                {
                                    _multiLineTokens.Remove(_multiLineTokens.Where(x => x.Classification == null && x.Tracking.GetStartPoint(snapshot) >= multiSpan.End).FirstOrDefault());
                                    i = _multiLineTokens.Count;
                                }
                                Invalidate(multiSpan);
                            }
                        }
                        else
                        {
                            if (_multiLineTokens[i].Classification != null && span.End.Position > _multiLineTokens[i].Tracking.GetStartPoint(snapshot).Position && span.Start.Position >= _multiLineTokens[i].Tracking.GetStartPoint(snapshot).Position)
                            {
                                list.Add(new ClassificationSpan(multiSpan, _multiLineTokens[i].Classification));
                                return list;
                            }
                            auxLanguage = _multiLineTokens[i].Language;
                            sectionDistances.Add(new Tuple<Languages, int>(auxLanguage, span.Start - multiSpan.Start));

                        }
                    }
                }
            }
            if (sectionDistances.Where(s => s.Item2 >= 0).Count() > 0)
            {
                language = sectionDistances.Where(s => s.Item2 >= 0).OrderBy(s => s.Item2).FirstOrDefault().Item1;
            }
            Debug.WriteLine("=============================");
            Debug.WriteLine(FlexTokenizer.Definitions.Count);

            //if (!isInsideMultiline || language == Languages.C || language == Languages.Flex)
            {
                int startPosition;
                int endPosition;
                int currentOffset = 0;
                string currentText = span.GetText();
                List<int[]> sections = new List<int[]>();
                foreach (var section in innerSections)
                {
                    sections.Add(new int[2] { section[0], section[1] });
                }
                do
                {
                    startPosition = span.Start.Position + currentOffset;
                    endPosition = startPosition;
                    for (int i = 0; i < sections.Count; i++)
                    {
                        sections[i][0] = innerSections[i][0] - startPosition;
                        sections[i][1] = innerSections[i][1] - startPosition;
                    }

                    var token = tokenizer.Scan(currentText, currentOffset, currentText.Length, ref language, ref ecase, sections, -1, 0);

                    if (token != null)
                    {
                        if (language == Languages.Flex && _multiLineTokens.Where(t => t.Tracking.GetStartPoint(snapshot).Position == startPosition && t.Language == Languages.Flex).Any())
                        {
                            token.State = 0;
                            token.TokenId = FlexTokenizer.Classes.Other;
                        }
                        if (token.State != (int)Cases.FlexDefinitions && token.State != (int)Cases.FlexRules && token.State != (int)Cases.C && token.State != (int)Cases.CEnding && token.State != (int)Cases.MultiLineComment)
                        {
                            endPosition = startPosition + token.Length;
                        }
                        if (ecase == Cases.CEnding)
                        {
                            startPosition += token.StartIndex;
                            endPosition = span.Snapshot.Length;
                        }
                        while (token != null && token.State != 0 && endPosition < span.Snapshot.Length)
                        {
                            int textSize = snapshot.Length - endPosition; //Math.Min(span.Snapshot.Length - endPosition, 1024);
                            currentText = span.Snapshot.GetText(endPosition, textSize);
                            token = tokenizer.Scan(currentText, 0, currentText.Length, ref language, ref ecase, sections, token.TokenId, token.State);
                            if (token != null)
                            {
                                endPosition += token.Length;
                            }
                        }
                        bool multiLineToken = false;
                        if (token.TokenId == FlexTokenizer.Classes.C || token.TokenId == FlexTokenizer.Classes.FlexDefinitions || token.TokenId == FlexTokenizer.Classes.CEnding)
                        {
                            if (endPosition < snapshot.Length)
                                endPosition -= 2;
                        }
                        IClassificationType classification = null;

                        switch (token.TokenId)
                        {
                            case 0:
                                classification = Classification.WhiteSpace;
                                break;
                            case 1:
                                classification = Classification.Keyword;
                                break;
                            case 2:
                                classification = Classification.Comment;
                                multiLineToken = true;
                                break;
                            case 3:
                                classification = Classification.Comment;
                                break;
                            case 4:
                                classification = Classification.NumberLiteral;
                                break;
                            case 5:
                                classification = Classification.StringLiteral;
                                break;
                            case 6:
                                classification = Classification.ExcludedCode;
                                break;
                            case 7:
                                classification = FlexDefinitionType;
                                break;
                            case -1:
                                classification = Classification.Other;
                                break;
                            case -2:
                                //classification = CSection;
                                multiLineToken = true;
                                break;
                            case -3:
                                //classification = FlexDefinitionSection;
                                multiLineToken = true;
                                break;
                            case -4:
                                //classification = CSection;
                                multiLineToken = true;
                                break;
                            case -5:
                                //classification = FlexSection;
                                multiLineToken = true;
                                break;
                            case -6:
                                //classification = CEnding;
                                multiLineToken = true;
                                break;
                            default:
                                break;
                        }

                        var tokenSpan = new SnapshotSpan(span.Snapshot, startPosition, (endPosition - startPosition));
                        if (classification != null)
                            list.Add(new ClassificationSpan(tokenSpan, classification));

                        if (multiLineToken)
                        {
                            if (!_multiLineTokens.Any(a => a.Tracking.GetSpan(span.Snapshot).Span == tokenSpan.Span))
                            {
                                SnapshotSpan lastSpan = new SnapshotSpan();
                                if (token.TokenId == FlexTokenizer.Classes.MultiLineComment)
                                {
                                    
                                    ClearTokenIntersections(tokenSpan, snapshot, true);
                                    MultiLineToken tokenToCheck = _multiLineTokens.Where( x => x.Tracking.GetEndPoint(snapshot).Position > tokenSpan.Start && x.Tracking.GetEndPoint(snapshot) < tokenSpan.End).FirstOrDefault();
                                    if (tokenToCheck != null)
                                    {
                                        _multiLineTokens = _multiLineTokens.OrderBy( x => x.Tracking.GetStartPoint(snapshot)).ToList();
                                        var lastToken = _multiLineTokens.Where(x => x.Classification == null && x.Tracking.GetStartPoint(snapshot).Position <= tokenToCheck.Tracking.GetStartPoint(snapshot) && x.Tracking.GetEndPoint(snapshot).Position >= tokenToCheck.Tracking.GetStartPoint(snapshot).Position && x != tokenToCheck).ToList();//_multiLineTokens.Where(x => x.Classification == null).OrderBy(x => x.Tracking.GetStartPoint(snapshot).Position).Last().Tracking.GetSpan(snapshot);
                                        if (lastToken.Count == 0)
                                            lastSpan = _multiLineTokens.Where(x => x.Classification == null).OrderBy(x => x.Tracking.GetStartPoint(snapshot)).First().Tracking.GetSpan(snapshot);
                                        else
                                            lastSpan = lastToken.First().Tracking.GetSpan(snapshot);
                                        for (int i = _multiLineTokens.Count - 1; i >= 0; i--)
                                        {
                                            if (_multiLineTokens[i].Tracking.GetStartPoint(snapshot).Position >= tokenToCheck.Tracking.GetStartPoint(snapshot).Position && _multiLineTokens[i].Classification == null)
                                            {
                                                //if (_multiLineTokens[i].Classification == null)
                                                //    lastSpan = _multiLineTokens.Where( x => x.Tracking.GetEndPoint(snapshot) == _multiLineTokens[i].Tracking.GetStartPoint(snapshot)).FirstOrDefault().Tracking.GetSpan(snapshot);
                                                _multiLineTokens.Remove(_multiLineTokens[i]);
                                            }
                                        }
                                        lastSpan = new SnapshotSpan(lastSpan.Start, new SnapshotPoint(snapshot, lastSpan.End.Position + (lastSpan.End.Position > snapshot.Length - 2 ? 0 : 2)));
                                        
                                        Invalidate(lastSpan);
                                    }
                                }
                                else
                                {
                                    ClearTokenIntersections(tokenSpan, snapshot);
                                }
                                if (GetLanguage(token.TokenId) == Languages.Flex && _multiLineTokens.Where(x => x.Language == Languages.Flex).Any())
                                    _multiLineTokens.Remove(_multiLineTokens.Where(x => x.Language == Languages.Flex).First());
                                _multiLineTokens.Add(new MultiLineToken()
                                {
                                    Classification = classification,
                                    Version = span.Snapshot.Version,
                                    Tracking = span.Snapshot.CreateTrackingSpan(tokenSpan.Span, SpanTrackingMode.EdgeExclusive),
                                    Language = GetLanguage(token.TokenId)
                                });
                                if (token.TokenId == FlexTokenizer.Classes.MultiLineComment)
                                {
                                    if (!lastSpan.IsEmpty)
                                    {
                                        GetClassificationSpans(lastSpan);
                                    }
                                }
                                if (token.TokenId < FlexTokenizer.Classes.Other)
                                {
                                    var auxSpan = new SnapshotSpan(tokenSpan.Start, tokenSpan.End.Add(tokenSpan.End > snapshot.Length - 2 ? 0 : 2));
                                    Invalidate(auxSpan);
                                    return list;
                                }
                                else if (tokenSpan.End > span.End)
                                {
                                    Invalidate(new SnapshotSpan(span.End + 1, tokenSpan.End));
                                    return list;
                                }
                            }
                        }
                        currentOffset += token.Length;
                    }
                    if (token == null)
                    {
                        break;
                    }
                } while (currentOffset < currentText.Length);
            }
            return list;
        }

        private MultiLineToken HandleFlexDefinitions(SnapshotSpan span, List<int[]> innerSections)
        {

            var mlt = GetLanguageSpan(new SnapshotSpan(span.Snapshot, new Span(0, span.Snapshot.Length)), innerSections, Languages.NoLanguage);
            return mlt;
        }

        public MultiLineToken GetLanguageSpan(SnapshotSpan span, List<int[]> innerSections, Languages l = Languages.FlexDefinitions)
        {
            var list = new List<ClassificationSpan>();
            bool isInsideMultiline = false;
            Cases ecase = Cases.NoCase;
            Languages language = l;
            ITextSnapshot snapshot = span.Snapshot;

            if (!isInsideMultiline)
            {
                int startPosition;
                int endPosition;
                int currentOffset = 0;
                string currentText = span.GetText();
                List<int[]> sections = new List<int[]>();
                foreach (var section in innerSections)
                {
                    sections.Add(new int[2] { section[0], section[1] });
                }
                do
                {
                    startPosition = span.Start.Position + currentOffset;
                    endPosition = startPosition;
                    for (int i = 0; i < sections.Count; i++)
                    {
                        sections[i][0] = innerSections[i][0] - startPosition;
                        sections[i][1] = innerSections[i][1] - startPosition;
                    }
                    startPosition = span.Start.Position + currentOffset;
                    endPosition = startPosition;
                    var token = tokenizer.Scan(currentText, currentOffset, currentText.Length, ref language, ref ecase, sections, -1, 0);

                    if (token != null)
                    {
                        if (language == Languages.Flex && _multiLineTokens.Where(t => t.Tracking.GetStartPoint(snapshot).Position == startPosition && t.Language == Languages.Flex).Any())
                        {
                            token.State = 0;
                            token.TokenId = FlexTokenizer.Classes.Other;
                        }
                        if (token.State != (int)Cases.FlexDefinitions && token.State != (int)Cases.C && token.State != (int)Cases.FlexRules && token.State != (int)Cases.CEnding)
                        {
                            endPosition = startPosition + token.Length;
                        }
                        while (token != null && token.State != 0 && endPosition < span.Snapshot.Length)
                        {
                            int textSize = snapshot.Length - endPosition; //Math.Min(span.Snapshot.Length - endPosition, 1024);
                            currentText = span.Snapshot.GetText(endPosition, textSize);
                            token = tokenizer.Scan(currentText, 0, currentText.Length, ref language, ref ecase, sections, token.TokenId, token.State);

                            if (token != null)
                            {
                                endPosition += token.Length;
                            }
                        }
                        bool multiLineToken = false;
                        if (token.TokenId == FlexTokenizer.Classes.C || token.TokenId == FlexTokenizer.Classes.FlexDefinitions /*|| token.TokenId == FlexTokenizer.Classes.FlexRules*/ || token.TokenId == FlexTokenizer.Classes.CEnding)
                        {
                            if (endPosition < snapshot.Length)
                                endPosition -= 2;
                        }
                        IClassificationType classification = null;

                        switch (token.TokenId)
                        {
                            case 0:
                                classification = Classification.WhiteSpace;
                                break;
                            case 1:
                                classification = Classification.Keyword;
                                break;
                            case 2:
                                classification = Classification.Comment;
                                multiLineToken = true;
                                break;
                            case 3:
                                classification = Classification.Comment;
                                break;
                            case 4:
                                classification = Classification.NumberLiteral;
                                break;
                            case 5:
                                classification = Classification.StringLiteral;
                                break;
                            case 6:
                                classification = Classification.ExcludedCode;
                                break;
                            case 7:
                                classification = FlexDefinitionType;
                                break;
                            case -1:
                                classification = Classification.Other;
                                break;
                            case -2:
                                multiLineToken = true;
                                break;
                            case -3:
                                multiLineToken = true;
                                break;
                            case -4:
                                multiLineToken = true;
                                break;
                            case -5:
                                multiLineToken = true;
                                break;
                            case -6:
                                multiLineToken = true;
                                break;
                            default:
                                break;
                        }

                        var tokenSpan = new SnapshotSpan(span.Snapshot, startPosition, (endPosition - startPosition));
                        if (token.TokenId >= FlexTokenizer.Classes.Other)
                            list.Add(new ClassificationSpan(tokenSpan, classification));

                        if (multiLineToken && classification == null)
                        {
                            //if (!_multiLineTokens.Any(a => a.Tracking.GetSpan(span.Snapshot).Span == tokenSpan.Span))
                            {
                                return new MultiLineToken()
                                {
                                    Classification = classification,
                                    Version = span.Snapshot.Version,
                                    Tracking = span.Snapshot.CreateTrackingSpan(tokenSpan.Span, SpanTrackingMode.EdgeExclusive),
                                    Language = GetLanguage(token.TokenId)
                                };

                            }
                        }
                        currentOffset += token.Length;
                    }
                    if (token == null)
                    {
                        break;
                    }
                } while (currentOffset < currentText.Length);
            }
            return null;
        }

        private void ClearTokenIntersections(SnapshotSpan mltSpan, ITextSnapshot snapshot, bool deleteComments = false)
        {
            MultiLineToken[] aux = new MultiLineToken[_multiLineTokens.Count];
            _multiLineTokens.CopyTo(aux);
            List<MultiLineToken> l = aux.ToList();

            foreach (var item in l)
            {
                if (mltSpan.Start <= item.Tracking.GetStartPoint(snapshot) && mltSpan.End >= item.Tracking.GetEndPoint(snapshot) && (item.Classification == null || deleteComments))
                {
                    _multiLineTokens.Remove(item);
                }
            }
        }
        private Languages GetLanguage(int value)
        {
            switch (value)
            {
                case -2:
                    return Languages.C;
                case -3:
                    return Languages.FlexDefinitions;
                case -4:
                    return Languages.C;
                case -5:
                    return Languages.Flex;
                case -6:
                    return Languages.CEnding;
                default:
                    return Languages.FlexDefinitions;
            }
        }

        #endregion
    }
}
