
using System;
using System.Collections.Generic;

namespace FerrumAddin
{
    [Serializable]
    public class WorksetByParameter : WorksetBy
    {
        public List<string> ParameterNames;

        public WorksetByParameter()
        {

        }
    }
}
