// Copyright (c) Xenko contributors (https://xenko.com) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using System;
using System.Threading.Tasks;
using Xenko.Core.Assets.Editor.Services;
using Xenko.Core.Assets.Editor.ViewModel.Progress;
using Xenko.Core.Assets.Templates;
using Xenko.Core.Translation;

namespace Xenko.Core.Assets.Editor.Components.TemplateDescriptions
{
    internal static class TemplateGeneratorHelper
    {
        /// <summary>
        /// Invokes the given template generator safely. The generator will be prepared on the calling thread, and run from
        /// a task. This methods will catch and log all exceptions occurring during the template generation.
        /// </summary>
        /// <param name="generator">The template generator to run.</param>
        /// <param name="parameters">The parameters for the template generator.</param>
        /// <param name="workProgress">The view model used to report progress.</param>
        /// <returns>A task that completes when the template generator has finished to run.</returns>
        internal static async Task<bool> RunTemplateGeneratorSafe<TParameters>(ITemplateGenerator<TParameters> generator, TParameters parameters, WorkProgressViewModel workProgress) where TParameters : TemplateGeneratorParameters
        {
            if (generator == null) throw new ArgumentNullException(nameof(generator));
            if (parameters == null) throw new ArgumentNullException(nameof(parameters));
            var success = false;
            try
            {
                success = await generator.PrepareForRun(parameters);
                if (!success)
                {
                    // If the preparation failed without error, it means that the user cancelled the operation.
                    if (!parameters.Logger.HasErrors)
                        parameters.Logger.Info(Tr._p("Log", "Operation cancelled."));

                    return false;
                }
            }
            catch (Exception e)
            {
                parameters.Logger.Error(Tr._p("Log", "An exception occurred while generating the template. Reopen it or just remove broken stuff."), e);
            }

            /*if (parameters.Logger.HasErrors || !success)
            {
                workProgress?.ServiceProvider.Get<IEditorDialogService>().ShowProgressWindow(workProgress, 0);
                return false;
            }*/

            workProgress?.ServiceProvider.Get<IEditorDialogService>().ShowProgressWindow(workProgress, 500);

            var result = await Task.Run(() =>
            {
                try
                {
                    return generator.Run(parameters);
                }
                catch (Exception e)
                {
                    parameters.Logger.Error(Tr._p("Log", "An exception occurred while generating the template. Reopen it or just remove broken stuff."), e);
                    return true;
                }
            });

            return true;
        }
    }
}
