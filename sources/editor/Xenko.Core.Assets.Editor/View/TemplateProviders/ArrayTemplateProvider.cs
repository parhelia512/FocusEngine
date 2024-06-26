// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using System;

using Xenko.Core.Presentation.Quantum;
using Xenko.Core.Presentation.Quantum.View;
using Xenko.Core.Presentation.Quantum.ViewModels;

namespace Xenko.Core.Assets.Editor.View.TemplateProviders
{
    public class ArrayTemplateProvider : NodeViewModelTemplateProvider
    {
        public override string Name => (ElementType?.Name ?? "") + "[]";

        public Type ElementType { get; set; }

        public override bool MatchNode(NodeViewModel node)
        {
            if (node.Type.IsArray)
            {
                return node.NodeValue != null;
            }
            return false;
        }
    }
}
