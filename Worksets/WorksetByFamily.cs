
using System;
using System.Collections.Generic;

namespace FerrumAddinDev
{
    [Serializable]
    public class WorksetByFamily : WorksetBy
    {
        public List<string> FamilyNames;

        public WorksetByFamily()
        {

        }
    }
}
