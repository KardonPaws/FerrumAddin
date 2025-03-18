
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FerrumAddin
{
    [Serializable]
    public class WorksetByLink
    {
        public string separator = "_";
        public int partNumberAfterSeparator = 0;
        public int ignoreFirstCharsAfterSeparation = 0;
        public int ignoreLastCharsAfterSeparation = 0;
        public string prefixForLinkWorksets = "#";
    }
}
