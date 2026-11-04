namespace DesktopIconGrid.Core;

/// <summary>
/// Maps the <c>shell:</c> names used by the special-item catalog to the CLSID parsing names that the
/// Windows shell item APIs accept, so icons can be resolved for virtual folders.
/// </summary>
public static class ShellLocations
{
    private static readonly Dictionary<string, string> ParsingNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["shell:MyComputerFolder"] = "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}",
        ["shell:NetworkPlacesFolder"] = "::{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}",
        ["shell:ConnectionsFolder"] = "::{7007ACC7-3202-11D1-AAD2-00805FC1270E}",
        ["shell:ControlPanelFolder"] = "::{26EE0668-A00A-44D7-9371-BEB064C98683}",
        ["shell:PrintersFolder"] = "::{2227A280-3AEA-1069-A2DE-08002B30309D}",
        ["shell:RecycleBinFolder"] = "::{645FF040-5081-101B-9F08-00AA002F954E}",
        ["shell:Desktop"] = "::{B4BFCC3A-DB2C-424C-B029-7FE99A87C641}",
        ["shell:Personal"] = "::{D3162B92-9365-467A-956B-92C05BC4D9E5}",
        ["shell:Downloads"] = "::{374DE290-123F-4565-9164-39C4925E467B}",
        ["shell:My Pictures"] = "::{24AD3AD4-A569-4530-98E1-AB02F9417AA8}",
    };

    public static bool TryGetParsingName(string location, out string parsingName)
    {
        if (!string.IsNullOrWhiteSpace(location) && ParsingNames.TryGetValue(location.Trim(), out var found))
        {
            parsingName = found;
            return true;
        }

        parsingName = string.Empty;
        return false;
    }
}
