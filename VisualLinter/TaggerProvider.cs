﻿using Microsoft.VisualStudio.Shell.TableControl;
using Microsoft.VisualStudio.Shell.TableManager;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Threading.Tasks;

namespace jwldnr.VisualLinter
{
    [Export(typeof(IViewTaggerProvider))]
    [TagType(typeof(IErrorTag))]
    [ContentType("text")]
    internal class TaggerProvider : IViewTaggerProvider, ITableDataSource
    {
        public string DisplayName => "VisualLint";
        public string Identifier => "VisualLint";
        public string SourceTypeIdentifier => StandardTableDataSources.ErrorTableDataSource;

        private readonly ILinterService _linterService;
        private readonly List<SinkManager> _managers = new List<SinkManager>();
        private readonly TaggerManager _taggers = new TaggerManager();
        private readonly ITextDocumentFactoryService _textDocumentFactoryService;

        [ImportingConstructor]
        internal TaggerProvider(
            [Import] ILinterService linterService,
            [Import] ITableManagerProvider tableManagerProvider,
            [Import] ITextDocumentFactoryService textDocumentFactoryService)
        {
            _linterService = linterService;

            var errorTableManager = tableManagerProvider
                .GetTableManager(StandardTables.ErrorsTable);

            _textDocumentFactoryService = textDocumentFactoryService;

            var columns = new[]
            {
                StandardTableColumnDefinitions.BuildTool,
                StandardTableColumnDefinitions.Column,
                StandardTableColumnDefinitions.DetailsExpander,
                StandardTableColumnDefinitions.DocumentName,
                StandardTableColumnDefinitions.ErrorCategory,
                StandardTableColumnDefinitions.ErrorCode,
                StandardTableColumnDefinitions.ErrorSeverity,
                StandardTableColumnDefinitions.ErrorSource,
                StandardTableColumnDefinitions.Line,
                StandardTableColumnDefinitions.Text
            };

            errorTableManager.AddSource(this, columns);
        }

        public ITagger<T> CreateTagger<T>(ITextView textView, ITextBuffer buffer) where T : ITag
        {
            if (buffer != textView.TextBuffer || typeof(IErrorTag) != typeof(T))
                return null;

            if (!TryGetTextDocument(textView.TextDataModel.DocumentBuffer, out var document))
                return null;

            var filePath = document.FilePath;
            var extension = Path.GetExtension(filePath)?.ToLowerInvariant();
            if (".js" != extension)
                return null;

            lock (_taggers)
            {
                if (!_taggers.Exists(filePath))
                    return new Tagger(buffer, document, this) as ITagger<T>;

                var result = _taggers.TryGetValue(filePath, out var tagger);
                if (false == result)
                    return null;

                return tagger as ITagger<T>;
            }
        }

        public IDisposable Subscribe(ITableDataSink sink)
        {
            return new SinkManager(this, sink);
        }

        internal void AddSinkManager(SinkManager manager)
        {
            lock (_managers)
            {
                _managers.Add(manager);

                foreach (var tagger in _taggers.Values)
                    manager.AddFactory(tagger.Factory);
            }
        }

        internal void AddTagger(Tagger tagger)
        {
            lock (_managers)
            {
                _taggers.Add(tagger);

                foreach (var manager in _managers)
                    manager.AddFactory(tagger.Factory);
            }
        }

        internal async Task AnalyzeAsync(string filePath)
        {
            var messages = await LintAsync(filePath);
            UpdateMessages(filePath, messages);
        }

        internal Task<IEnumerable<LinterMessage>> LintAsync(string filePath)
        {
            return _linterService.LintAsync(filePath);
        }

        internal void RemoveSinkManager(SinkManager manager)
        {
            lock (_managers)
            {
                _managers.Remove(manager);
            }
        }

        internal void RemoveTagger(Tagger tagger)
        {
            lock (_managers)
            {
                _taggers.Remove(tagger);

                foreach (var manager in _managers)
                    manager.RemoveFactory(tagger.Factory);
            }
        }

        internal void Rename(string oldPath, string newPath)
        {
            lock (_taggers)
            {
                _taggers.Rename(oldPath, newPath);
            }
        }

        internal void UpdateAllSinks()
        {
            lock (_managers)
            {
                foreach (var manager in _managers)
                    manager.UpdateSink();
            }
        }

        private bool TryGetTextDocument(ITextBuffer buffer, out ITextDocument document)
        {
            return _textDocumentFactoryService.TryGetTextDocument(buffer, out document);
        }

        private void UpdateMessages(string filePath, IEnumerable<LinterMessage> messages)
        {
            lock (_taggers)
            {
                if (_taggers.TryGetValue(filePath, out Tagger tagger))
                    tagger.UpdateMessages(messages);
            }
        }
    }
}