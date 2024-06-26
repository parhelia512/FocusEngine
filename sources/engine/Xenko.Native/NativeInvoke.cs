// Copyright (c) Xenko contributors (https://xenko.com) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using Xenko.Core;
using Xenko.Core.Mathematics;
using Xenko.Core.Reflection;

namespace Xenko.Native
{
    internal static class NativeInvoke
    {
#if XENKO_PLATFORM_IOS
        internal const string Library = "__Internal";
#else
        internal const string Library = "libxenko";
#endif

        internal static void PreLoad()
        {
#if XENKO_PLATFORM_WINDOWS
            NativeLibrary.PreloadLibrary(Library + ".dll", typeof(NativeInvoke));
#else
            NativeLibrary.PreloadLibrary(Library + ".so", typeof(NativeInvoke));
#endif
        }

        static NativeInvoke()
        {
            PreLoad();
        }

        [SuppressUnmanagedCodeSecurity]
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        public static extern void UpdateBufferValuesFromElementInfo(IntPtr drawInfo, IntPtr vertexPtr, IntPtr indexPtr, int vertexOffset);

        [SuppressUnmanagedCodeSecurity]
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        public static extern void xnGraphicsFastTextRendererGenerateVertices(RectangleF constantInfos, RectangleF renderInfos, string text, out IntPtr textLength, out IntPtr vertexBufferPointer);
    }

    internal class Module
    {
        [ModuleInitializer]
        public static void Initialize()
        {

        }
    }
}
