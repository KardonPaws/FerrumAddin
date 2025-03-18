
using System;
using System.Collections.Generic;

namespace FerrumAddin
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
