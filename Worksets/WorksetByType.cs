
using System;
using System.Collections.Generic;

namespace FerrumAddin
{
    [Serializable]
    public class WorksetByType : WorksetBy
    {
        public List<string> TypeNames;

        public WorksetByType()
        {

        }
    }
}
