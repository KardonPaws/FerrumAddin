using System;
using System.Collections.Generic;

namespace FerrumAddin
{
    [Serializable]
    public class InfosStorage
    {
        public string ConfigurationName;

        public List<WorksetByCategory> worksetsByCategory;
        public List<WorksetByFamily> worksetsByFamily;
        public List<WorksetByType> worksetsByType;
        public List<WorksetByParameter> worksetByParameter;
        public WorksetByLink worksetByLink;
        public WorksetByRazd worksetByRazd;

        public InfosStorage()
        {

        }
    }
}
