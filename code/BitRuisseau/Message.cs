using System;
using System.Collections.Generic;

namespace BitRuisseau
{
    // DTO sérialisable (pas d'interface)
    public class SongDto
    {
        public string Path { get; set; } = "";
        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
        public int Year { get; set; }
        public int Size { get; set; }
        public string[] Featuring { get; set; } = Array.Empty<string>();
        public string Hash { get; set; } = "";
        public TimeSpan Duration { get; set; }
        public string Extension { get; set; } = "";
    }

    public class Message
    {
        public string Recipient { get; set; } = "";
        public string Sender { get; set; } = "";
        public string Action { get; set; } = "";

        public int? StartByte { get; set; }
        public int? EndByte { get; set; }

        // IMPORTANT: on remplace List<ISong> par List<SongDto>
        public List<SongDto>? SongList { get; set; }

        public string? SongData { get; set; }
        public string? Hash { get; set; }

        // Ultra bete test
        public string? RequestId { get; set; }
    }
}
