using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace vmPing.Classes
{
    public static class InventoryStore
    {
        public static string FilePath => Path.Combine(
            Path.GetDirectoryName(Configuration.FilePath) ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "inventory.xml");

        public static void Save(List<DeviceInventory> devices)
        {
            try
            {
                var doc = new XDocument(
                    new XElement("inventory",
                        new XElement("generated", DateTime.UtcNow.ToString("o")),
                        new XElement("devices",
                            devices.Select(d => d.ToXml()))));
                doc.Save(FilePath);
            }
            catch
            {
                // Persistence is best-effort.
            }
        }

        public static List<DeviceInventory> Load()
        {
            var devices = new List<DeviceInventory>();
            try
            {
                if (!File.Exists(FilePath))
                {
                    return devices;
                }
                var doc = XDocument.Load(FilePath);
                devices = doc.Root?.Element("devices")?
                    .Elements("device")
                    .Select(DeviceInventory.FromXml)
                    .ToList() ?? new List<DeviceInventory>();
            }
            catch
            {
                // Loading is best-effort.
            }
            return devices;
        }
    }
}