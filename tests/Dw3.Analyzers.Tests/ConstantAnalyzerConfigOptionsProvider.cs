using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Dw3.Analyzers.Tests;

/// <summary>
/// A minimal <see cref="AnalyzerConfigOptionsProvider"/> that returns a single fixed
/// key-value pair for the specified file path and empty options for everything else.
/// Used in unit tests to inject a configurable threshold without a real .editorconfig file.
/// </summary>
internal sealed class ConstantAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
{
    /// <summary>Empty options returned for all files not matching <see cref="_filePath"/>.</summary>
    private static readonly AnalyzerConfigOptions EmptyOptions = new EmptyAnalyzerConfigOptions();

    /// <summary>The normalised file path this provider applies to.</summary>
    private readonly string _filePath;

    /// <summary>The editorconfig key to expose.</summary>
    private readonly string _key;

    /// <summary>The value to return for <see cref="_key"/>.</summary>
    private readonly string _value;

    /// <summary>
    /// Initializes a new instance of <see cref="ConstantAnalyzerConfigOptionsProvider"/>.
    /// </summary>
    /// <param name="filePath">The source file path the options apply to.</param>
    /// <param name="key">The editorconfig key to expose.</param>
    /// <param name="value">The value to expose for <paramref name="key"/>.</param>
    internal ConstantAnalyzerConfigOptionsProvider(string filePath, string key, string value)
    {
        _filePath = filePath;
        _key = key;
        _value = value;
    }

    /// <inheritdoc/>
    public override AnalyzerConfigOptions GlobalOptions => EmptyOptions;

    /// <inheritdoc/>
    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)
    {
        // Match on the leaf file name to avoid path-separator differences across platforms.
        var separator = new[] { '/', '\\' };
        var treeName = tree.FilePath.Split(separator).LastOrDefault() ?? string.Empty;
        var targetName = _filePath.Split(separator).LastOrDefault() ?? string.Empty;

        return string.Equals(treeName, targetName, System.StringComparison.OrdinalIgnoreCase)
            ? new SingleKeyOptions(_key, _value)
            : EmptyOptions;
    }

    /// <inheritdoc/>
    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => EmptyOptions;

    // -------------------------------------------------------------------------
    // Inner types
    // -------------------------------------------------------------------------

    /// <summary>Options that expose exactly one key-value pair.</summary>
    private sealed class SingleKeyOptions : AnalyzerConfigOptions
    {
        /// <summary>The exposed key.</summary>
        private readonly string _key;

        /// <summary>The exposed value.</summary>
        private readonly string _value;

        /// <summary>
        /// Initializes a new instance of <see cref="SingleKeyOptions"/>.
        /// </summary>
        internal SingleKeyOptions(string key, string value)
        {
            _key = key;
            _value = value;
        }

        /// <inheritdoc/>
        public override bool TryGetValue(string key, out string value)
        {
            if (string.Equals(key, _key, System.StringComparison.OrdinalIgnoreCase))
            {
                value = _value;
                return true;
            }

            value = null!;
            return false;
        }
    }

    /// <summary>Options that never match any key.</summary>
    private sealed class EmptyAnalyzerConfigOptions : AnalyzerConfigOptions
    {
        /// <inheritdoc/>
        public override bool TryGetValue(string key, out string value)
        {
            value = null!;
            return false;
        }
    }
}
