using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using VERMAXION.IPC;

namespace VERMAXION.CustomDeliveries;

// Host adapter for VSatisfy's feature source; no separate plugin or configuration file.
internal static class Service
{
    internal static VNavmeshIPC Navigation { get; set; } = null!;
    public static IPluginLog Log => Plugin.Log;
    public static IDataManager DataManager => Plugin.DataManager;
    public static IGameInteropProvider Hook => Plugin.GameInterop;
    public static ICondition Conditions => Plugin.Condition;
    public static IFramework Framework => Plugin.Framework;
    public static IClientState ClientState => Plugin.ClientState;
    public static IPlayerState PlayerState => Plugin.PlayerState;
    public static IDalamudPluginInterface PluginInterface => Plugin.PluginInterface;
    public static IObjectTable Objects => Plugin.ObjectTable;
    public static Lumina.Excel.ExcelSheet<T>? LuminaSheet<T>() where T : struct, Lumina.Excel.IExcelRow<T>
        => DataManager.GetExcelSheet<T>(Dalamud.Game.ClientLanguage.English);
    public static Lumina.Excel.SubrowExcelSheet<T>? LuminaSheetSubrow<T>() where T : struct, Lumina.Excel.IExcelSubrow<T>
        => DataManager.GetSubrowExcelSheet<T>(Dalamud.Game.ClientLanguage.English);
    public static T? LuminaRow<T>(uint row) where T : struct, Lumina.Excel.IExcelRow<T>
        => LuminaSheet<T>()?.GetRowOrDefault(row);
    public static Lumina.Excel.SubrowCollection<T>? LuminaSubrows<T>(uint row) where T : struct, Lumina.Excel.IExcelSubrow<T>
        => LuminaSheetSubrow<T>()?.GetRowOrDefault(row);
    public static T? LuminaRow<T>(uint row, ushort subRow) where T : struct, Lumina.Excel.IExcelSubrow<T>
        => LuminaSheetSubrow<T>()?.GetSubrowOrDefault(row, subRow);
}
