using System.Collections.Generic;

namespace SkiaSharp.Unity {
    public class SkottieMarkers {
        public List<state> markers = new List<state>();
        public class state {
            public int tm;
            public int dr;
            public string cm; 
        }

        public state GetStateByName(string name) {
            if (markers.Count == 0) {
                return null;
            }
            
            foreach (var st in markers) {
                if (st.cm.Equals(name)) {
                    return st;
                }
            }

            return null;
        }
    }
}
