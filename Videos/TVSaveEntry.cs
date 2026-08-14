using System.Collections.Generic;

namespace Boxroom_TV.Videos
{
    public class TVSaveEntry
    {
        public List<string> VideoFiles = new List<string>();
        public int CurrentIndex;
        public double PlaybackTime;
        public float Brightness = 1f;
        public bool IsOn = true;
    }
}