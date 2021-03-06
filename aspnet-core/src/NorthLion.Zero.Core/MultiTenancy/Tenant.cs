﻿using Abp.MultiTenancy;
using NorthLion.Zero.Authorization.Users;

namespace NorthLion.Zero.MultiTenancy
{
    public class Tenant : AbpTenant<User>
    {
        public Tenant()
        {
            
        }

        public Tenant(string tenancyName, string name)
            : base(tenancyName, name)
        {
        }
    }
}