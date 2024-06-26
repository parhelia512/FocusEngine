// Copyright (c) Xenko contributors (https://xenko.com) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xenko.Core.Mathematics;
using Xenko.Assets.Presentation.AssetEditors.EntityHierarchyEditor.Game;
using Xenko.Assets.Presentation.AssetEditors.GameEditor.Services;
using Xenko.Assets.Presentation.AssetEditors.Gizmos;
using Xenko.Editor.EditorGame.Game;
using Xenko.Rendering;
using Xenko.Rendering.Compositing;

namespace Xenko.Assets.Presentation.AssetEditors.GameEditor.Game
{
    public class EditorGameGridService<TGridGizmo> : EditorGameServiceBase, IEditorGameGridViewModelService
        where TGridGizmo : GridGizmoBase, new()
    {
        private TGridGizmo grid;
        private EntityHierarchyEditorGame game;

        public override bool IsActive { get; set; } = true;

        public Color3 Color { get; set; } = (Color3)new Color(180, 180, 180);

        public float Alpha { get; set; } = 0.35f;

        public int AxisIndex { get; set; } = 1;

        public override IEnumerable<Type> Dependencies { get { yield return typeof(IEditorGameCameraService); } }

        protected override Task<bool> Initialize(EditorServiceGame editorGame)
        {
            if (editorGame == null) throw new ArgumentNullException(nameof(editorGame));
            game = (EntityHierarchyEditorGame)editorGame;

            var gridGizmoRenderStage = new RenderStage("GridGizmo", "Main") { SortMode = new BackToFrontSortMode() };
            game.EditorSceneSystem.GraphicsCompositor.RenderStages.Add(gridGizmoRenderStage);

            var meshRenderFeature = game.EditorSceneSystem.GraphicsCompositor.RenderFeatures.OfType<MeshRenderFeature>().First();
            meshRenderFeature.RenderStageSelectors.Add(new SimpleGroupToRenderStageSelector
            {
                EffectName = EditorGraphicsCompositorHelper.EditorForwardShadingEffect,
                RenderGroup = GridGizmoBase.GridGroupMask,
                RenderStage = gridGizmoRenderStage
            });

            meshRenderFeature.PipelineProcessors.Add(new AlphaBlendPipelineProcessor { RenderStage = gridGizmoRenderStage });

            var editorCompositor = (EditorTopLevelCompositor)game.EditorSceneSystem.GraphicsCompositor.Game;
            editorCompositor.PostGizmoCompositors.Add(new SingleStageRenderer { RenderStage = gridGizmoRenderStage });

            grid = new TGridGizmo();
            grid.Initialize(editorGame.Services, game.EditorScene);
            game.Script.AddTask(UpdateGrid);
            return Task.FromResult(true);
        }

        private async Task UpdateGrid()
        {
            while (!IsDisposed)
            {
                grid.IsEnabled = IsActive;
                grid.Update(Color, Alpha, AxisIndex, game.EditorServices.Get<IEditorGameCameraService>().SceneUnit);
                await game.Script.NextFrame();
            }
        }
    }
}
