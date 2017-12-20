﻿/***********************************************************************
Copyright 2017 CodeX Enterprises LLC

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.

Major Changes:
12/2017    0.2     Initial release (Joel Champagne)
***********************************************************************/
using System;

namespace CodexMicroORM.Core
{
    public enum WrappingAction
    {
        NoneOrProvisionedAlready = 0,
        PreCodeGen = 1,
        Dynamic = 2,
        DataTable = 3
    }

    public enum ScopeMode
    {
        UseAmbient = 0,
        CreateNew = 1
    }

    [Flags]
    public enum WrappingSupport
    {
        None = 0,
        Notifications = 1,
        OriginalValues = 2,
        PropertyBag = 4,
        All = 7
    }

    public enum MergeBehavior
    {
        SilentMerge = 0,
        FailIfDifferent = 1
    }

    public enum DeleteCascadeAction
    {
        None = 0,
        Cascade = 1,
        SetNull = 2,
        Fail = 3
    }
}
