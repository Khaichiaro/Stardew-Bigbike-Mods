using StardewModdingAPI;
using StardewModdingAPI.Utilities;

namespace StardewBigbikeMod
{
    /// <summary>API ของ Generic Mod Config Menu (คัดมาเฉพาะ method ที่ใช้)
    /// ดูฉบับเต็มได้ที่ https://github.com/spacechase0/StardewValleyMods/blob/develop/GenericModConfigMenu/IGenericModConfigMenuApi.cs</summary>
    public interface IGenericModConfigMenuApi
    {
        /// <summary>ลงทะเบียน mod เข้าเมนู config</summary>
        void Register(IManifest mod, Action reset, Action save, bool titleScreenOnly = false);

        /// <summary>เพิ่มช่องปรับตัวเลข</summary>
        void AddNumberOption(IManifest mod, Func<int> getValue, Action<int> setValue, Func<string> name, Func<string>? tooltip = null, int? min = null, int? max = null, int? interval = null, Func<int, string>? formatValue = null, string? fieldId = null);

        /// <summary>เพิ่มช่องปรับตัวเลขทศนิยม</summary>
        void AddNumberOption(IManifest mod, Func<float> getValue, Action<float> setValue, Func<string> name, Func<string>? tooltip = null, float? min = null, float? max = null, float? interval = null, Func<float, string>? formatValue = null, string? fieldId = null);

        /// <summary>เพิ่มหัวข้อคั่นกลุ่มตัวเลือก</summary>
        void AddSectionTitle(IManifest mod, Func<string> text, Func<string>? tooltip = null);

        /// <summary>เพิ่มช่องตั้งปุ่มลัด (รองรับทั้งคีย์บอร์ดและจอย)</summary>
        void AddKeybindList(IManifest mod, Func<KeybindList> getValue, Action<KeybindList> setValue, Func<string> name, Func<string>? tooltip = null, string? fieldId = null);
    }
}
