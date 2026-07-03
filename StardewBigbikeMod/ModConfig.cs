using StardewModdingAPI;
using StardewModdingAPI.Utilities;

namespace StardewBigbikeMod
{
    /// <summary>ค่าตั้งของ mod — SMAPI จะ serialize เป็น config.json ในโฟลเดอร์ mod ให้อัตโนมัติ</summary>
    public sealed class ModConfig
    {
        /// <summary>ปุ่มดับเครื่องยนต์ (รองรับหลายปุ่ม คั่นด้วยจุลภาค ทั้งคีย์บอร์ดและจอย)</summary>
        public KeybindList EngineOffKey { get; set; } = new(
            new Keybind(SButton.K),
            new Keybind(SButton.ControllerBack));

        /// <summary>ค่าก่อสร้างโรงรถ (G)</summary>
        public int BuildCost { get; set; } = 10000;

        /// <summary>จำนวนไม้ที่ใช้สร้าง</summary>
        public int WoodAmount { get; set; } = 150;

        /// <summary>จำนวนหินที่ใช้สร้าง</summary>
        public int StoneAmount { get; set; } = 100;

        /// <summary>จำนวนวันที่ Robin ใช้สร้าง</summary>
        public int BuildDays { get; set; } = 2;
    }
}
