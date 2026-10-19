using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BassesModManager
{
    /// <summary>
    /// Reads the header of a Frosty mod (.fbmod, format versions 1 to 5) without the
    /// payload: the game profile, the mod details and the resource list with each
    /// resource's type, name, handler and the bundles it is added to. That is everything
    /// the launch needs to know; Auric reads the same header in the game and serves the
    /// payload. The layout follows Frosty's FrostyModReader and BaseModResource.Read,
    /// with the per-type fields of ResResource, ChunkResource and BundleResource skipped.
    /// </summary>
    internal sealed class FbModHeader
    {
        /// <summary>"FROSTY\0\1" as a little-endian ulong.</summary>
        public const ulong Magic = 0x01005954534F5246;

        /// <summary>The newest format version Frosty 1.0.6 writes.</summary>
        public const uint MaxVersion = 5;

        public enum ResourceType : byte
        {
            Embedded = 0,
            Ebx = 1,
            Res = 2,
            Chunk = 3,
            Bundle = 4
        }

        public sealed class Resource
        {
            public ResourceType Type;
            public string Name;

            /// <summary>-1 for a reference without payload.</summary>
            public int DataIndex;

            /// <summary>Frosty's Fnv1 of the custom handler's name, or 0 when the resource is finished data.</summary>
            public int Handler;

            /// <summary>Hashes of the bundles the resource is added to (Frosty's Add action).</summary>
            public List<int> AddedBundles = new List<int>();

            public bool HasHandler => Handler != 0;
        }

        public uint Version { get; private set; }

        /// <summary>The Frosty profile the mod was built for, as stored (lowercase, e.g. "starwarsbattlefront").</summary>
        public string Profile { get; private set; }

        public int GameVersion { get; private set; }
        public string Title { get; private set; }
        public string Author { get; private set; }
        public string Category { get; private set; }
        public string ModVersion { get; private set; }
        public string Description { get; private set; }
        public string Link { get; private set; }
        public List<Resource> Resources { get; } = new List<Resource>();

        /// <summary>
        /// Reads the header. Throws <see cref="InvalidDataException"/> for a file that is not
        /// a new-format Frosty mod (the legacy DbObject format, or a newer version than this
        /// reader knows), and the usual IO exceptions for a file that cannot be read.
        /// </summary>
        public static FbModHeader Read(string path)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                return Read(reader);
            }
        }

        private static FbModHeader Read(BinaryReader reader)
        {
            ulong magic = reader.ReadUInt64();
            if (magic != Magic)
                throw new InvalidDataException("not a Frosty mod in the current format (legacy format or not a .fbmod)");

            var header = new FbModHeader { Version = reader.ReadUInt32() };
            if (header.Version > MaxVersion)
                throw new InvalidDataException("mod format version " + header.Version + " is newer than this app knows");

            reader.ReadInt64();   // data offset
            reader.ReadInt32();   // data count

            header.Profile = ReadSizedString(reader, reader.ReadByte());
            header.GameVersion = reader.ReadInt32();

            header.Title = ReadNullTerminatedString(reader);
            header.Author = ReadNullTerminatedString(reader);
            header.Category = ReadNullTerminatedString(reader);
            header.ModVersion = ReadNullTerminatedString(reader);
            header.Description = ReadNullTerminatedString(reader);
            header.Link = header.Version >= 5 ? ReadNullTerminatedString(reader) : "";

            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
                header.Resources.Add(ReadResource(reader, header.Version));

            return header;
        }

        private static Resource ReadResource(BinaryReader reader, uint version)
        {
            var resource = new Resource { Type = (ResourceType)reader.ReadByte() };
            if (resource.Type > ResourceType.Bundle)
                throw new InvalidDataException("unknown resource type " + (byte)resource.Type);

            // BaseModResource.Read
            resource.DataIndex = reader.ReadInt32();
            if ((version <= 3 && resource.DataIndex != -1) || version > 3)
                resource.Name = ReadNullTerminatedString(reader);

            if (resource.DataIndex != -1)
            {
                reader.ReadBytes(20);         // sha1
                reader.ReadInt64();           // size
                reader.ReadByte();            // flags
                resource.Handler = reader.ReadInt32();
                if (version >= 3)
                    ReadNullTerminatedString(reader);   // user data
            }

            if (version <= 3 && resource.DataIndex != -1)
            {
                // Before version 4 the bundles the asset already lived in were stored too
                int existing = reader.ReadInt32();
                for (int i = 0; i < existing; i++)
                    reader.ReadInt32();
                ReadBundleList(reader, resource.AddedBundles);
            }
            else if (version > 3)
            {
                ReadBundleList(reader, resource.AddedBundles);
            }

            // The per-type tails, read past rather than kept
            switch (resource.Type)
            {
                case ResourceType.Res:
                    reader.ReadUInt32();                        // res type
                    reader.ReadUInt64();                        // res rid
                    reader.ReadBytes(reader.ReadInt32());       // res meta
                    break;
                case ResourceType.Chunk:
                    reader.ReadBytes(6 * 4);                    // range start/end, logical offset/size, h32, first mip
                    break;
                case ResourceType.Bundle:
                    resource.Name = ReadNullTerminatedString(reader);
                    reader.ReadInt32();                         // super bundle name hash
                    break;
            }

            return resource;
        }

        private static void ReadBundleList(BinaryReader reader, List<int> into)
        {
            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
                into.Add(reader.ReadInt32());
        }

        // Frosty reads these byte by byte as chars, so the same here: no UTF-8 decoding
        private static string ReadNullTerminatedString(BinaryReader reader)
        {
            var sb = new StringBuilder();
            while (true)
            {
                byte b = reader.ReadByte();
                if (b == 0)
                    return sb.ToString();
                sb.Append((char)b);
            }
        }

        private static string ReadSizedString(BinaryReader reader, int length)
        {
            var sb = new StringBuilder(length);
            for (int i = 0; i < length; i++)
            {
                byte b = reader.ReadByte();
                if (b != 0)
                    sb.Append((char)b);
            }
            return sb.ToString();
        }
    }
}
