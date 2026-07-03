namespace StardewBigbikeMod
{
    /// <summary>ข้อความ sync สถานะคนซ้อนท้ายข้ามเครื่อง (ส่งผ่าน SMAPI ModMessage)</summary>
    public sealed class PassengerMsg
    {
        /// <summary>UniqueMultiplayerID ของผู้เล่นที่ซ้อน</summary>
        public long PlayerId { get; set; }

        /// <summary>HorseId ของบิ๊กไบค์ที่ถูกซ้อน</summary>
        public Guid HorseId { get; set; }

        /// <summary>true = ขึ้นซ้อน, false = ลงจากรถ</summary>
        public bool Mounted { get; set; }
    }
}
