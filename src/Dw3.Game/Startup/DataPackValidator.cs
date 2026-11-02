using Dw3.Data;

namespace Dw3Production.Startup;

/// <summary>Tells whether the extracted data pack can start a session.</summary>
internal static class DataPackValidator
{
    /// <summary>
    /// Opens and closes the pack database. A missing, incomplete, schema-mismatched or corrupt
    /// pack makes the host show the first-launch extraction screen instead of failing later.
    /// </summary>
    /// <param name="dataRoot">Directory of the extracted data pack.</param>
    /// <returns><see langword="true"/> when the pack opened; <see langword="false"/> on any <see cref="DataPackException"/>.</returns>
    public static bool IsUsable(string dataRoot)
    {
        try
        {
            new DataPackReader(dataRoot).OpenDatabase().Dispose();
            return true;
        }
        catch (DataPackException)
        {
            return false;
        }
    }
}
