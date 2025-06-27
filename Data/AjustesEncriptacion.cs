namespace InventarioComputo.Data
{
    public class AjustesEncriptacion
    {
        public class EncryptionSettings
        {
            public string AesKey { get; set; }
            public string AesIV { get; set; }
            public byte[] GetKey() => Convert.FromBase64String(AesKey);
            public byte[] GetIV() => Convert.FromBase64String(AesIV);
        }
    }
}
