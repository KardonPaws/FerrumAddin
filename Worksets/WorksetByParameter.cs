
using System;
using System.Collections.Generic;

namespace FerrumAddinDev
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
