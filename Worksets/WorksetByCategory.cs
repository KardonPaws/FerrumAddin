
using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace FerrumAddinDev
{
    [Serializable]
    public class WorksetByCategory : WorksetBy
    {
        public List<BuiltInCategory> revitCategories;

        public WorksetByCategory()
        {

        }
    }
}
