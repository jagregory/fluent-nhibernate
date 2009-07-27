using System;
using System.Collections.Generic;
using FluentNHibernate.Conventions;

namespace FluentNHibernate.AutoMap
{
    public class PrivateAutoPersistenceModel : AutoPersistenceModel
    {
        public PrivateAutoPersistenceModel()
        {
            autoMapper = new PrivateAutoMapper(Expressions, new DefaultConventionFinder(), inlineOverrides);
        }
    }
}