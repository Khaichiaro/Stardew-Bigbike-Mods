namespace StardewBigbikeMod
{
    /// <summary>ค่าตั้งของ mod — SMAPI จะ serialize เป็น config.json ในโฟลเดอร์ mod ให้อัตโนมัติ</summary>
    public sealed class ModConfig
    {
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
