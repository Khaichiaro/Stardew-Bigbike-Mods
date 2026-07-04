using StardewModdingAPI;
using StardewModdingAPI.Utilities;

namespace StardewBigbikeMod
{
    /// <summary>ค่าตั้งของ mod — SMAPI จะ serialize เป็น config.json ในโฟลเดอร์ mod ให้อัตโนมัติ</summary>
    public sealed class ModConfig
    {
        /// <summary>ปุ่มดับเครื่องยนต์ (รองรับหลายปุ่ม ทั้งคีย์บอร์ดและจอย)</summary>
        public KeybindList EngineOffKey { get; set; } = new(
            new Keybind(SButton.K),
            new Keybind(SButton.ControllerBack));

        /// <summary>ปุ่มเบิ้ลเครื่อง (เร่งเครื่องอยู่กับที่)</summary>
        public KeybindList RevKey { get; set; } = new(
            new Keybind(SButton.R),
            new Keybind(SButton.RightStick));

        /// <summary>ความเร็วต่ำสุด (เกียร์ 1)</summary>
        public float MinSpeed { get; set; } = 1.5f;

        /// <summary>ความเร็วสูงสุด (เกียร์ 6)</summary>
        public float MaxSpeed { get; set; } = 8f;

        /// <summary>ระดับเสียงเครื่องยนต์ (0 = เงียบ, 1 = ปกติ, 2 = ดังสุด)</summary>
        public float EngineVolume { get; set; } = 1f;

        /// <summary>ค่าก่อสร้างโรงรถ (G)</summary>
        public int BuildCost { get; set; } = 10000;

        /// <summary>จำนวนเหล็กแท่ง (Iron Bar) ที่ใช้สร้าง</summary>
        public int IronBars { get; set; } = 5;

        /// <summary>จำนวนอีรีเดียมแท่ง (Iridium Bar) ที่ใช้สร้าง</summary>
        public int IridiumBars { get; set; } = 1;

        /// <summary>จำนวนแบตเตอรี่ (Battery Pack) ที่ใช้สร้าง</summary>
        public int Batteries { get; set; } = 1;

        /// <summary>จำนวนวันที่ Robin ใช้สร้าง</summary>
        public int BuildDays { get; set; } = 2;

        // หมายเหตุ: ตำแหน่งคนขับ/คนซ้อน fix ค่าตายตัวในโค้ด (DriverSeat/PassengerSeat) ไม่ทำเป็น config
        // เพราะ offset เป็นค่า render เฉพาะเครื่อง ถ้าให้แต่ละเครื่องปรับเองจะเห็นตำแหน่งไม่ตรงกันข้ามเครื่อง
    }
}
