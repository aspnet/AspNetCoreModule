// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace AspNetCoreModule.Test.Framework
{
    public class TestUtility
    {
        public static bool VerboseMode = true;
        public static bool ReadSlowly = false;

        public static void LogInformation(string format, params object[] parameters)
        {
            if (VerboseMode)
            {
                if (ReadSlowly && format.Contains("ReadDataCallback"))
                {
                    System.Threading.Thread.Sleep(3000);
                }
                Console.WriteLine(format, parameters);
            }
        }
    }   
}