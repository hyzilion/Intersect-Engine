﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Intersect.Client.Classes.MonoGame.Graphics;
using Intersect.Client.Framework.File_Management;
using Intersect.Client.Framework.GenericClasses;
using Intersect.Client.Framework.Graphics;
using Intersect.Client.General;
using Intersect.Client.Localization;
using Intersect.Client.UI;
using JetBrains.Annotations;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using XNARectangle = Microsoft.Xna.Framework.Rectangle;
using XNAColor = Microsoft.Xna.Framework.Color;

namespace Intersect.Client.MonoGame.Graphics
{
    public class MonoRenderer : GameRenderer
    {
        private ContentManager mContentManager;
        private GameBlendModes mCurrentBlendmode = GameBlendModes.None;
        private GameShader mCurrentShader;
        private FloatRect mCurrentSpriteView;
        private GameRenderTexture mCurrentTarget;
        private BlendState mCutoutState;
        private int mFps;
        private int mFpsCount;
        private long mFpsTimer;
        private Game mGame;
        [NotNull] private GameWindow mGameWindow;
        private bool mInitialized;
        private BlendState mNormalState;
        private BlendState mMultiplyState;
        RasterizerState mRasterizerState = new RasterizerState() {ScissorTestEnable = true};
        private bool mSpriteBatchBegan;
        [NotNull] private readonly List<MonoTexture> mAllTextures = new List<MonoTexture>();
        private long mFsChangedTimer = -1;
        private FloatRect mCurrentView;
        private int mDisplayHeight;
        private bool mDisplayModeChanged = false;
        private int mDisplayWidth;
        private GraphicsDeviceManager mGraphics;
        private GraphicsDevice mGraphicsDevice;

        private bool mInitializing;
        private DisplayMode mOldDisplayMode;
        private int mScreenHeight;
        private int mScreenWidth;

        private SpriteBatch mSpriteBatch;
        private List<string> mValidVideoModes;
        private GameRenderTexture mWhiteTexture;

        private BasicEffect mBasicEffect;

        public MonoRenderer(GraphicsDeviceManager graphics, ContentManager contentManager, [NotNull] Game monoGame)
        {
            mGame = monoGame;
            mGraphics = graphics;
            mContentManager = contentManager;

            mNormalState = new BlendState()
            {
                ColorSourceBlend = Blend.SourceAlpha,
                AlphaSourceBlend = Blend.One,
                ColorDestinationBlend = Blend.InverseSourceAlpha,
                AlphaDestinationBlend = Blend.InverseSourceAlpha
            };

            mMultiplyState = new BlendState()
            {
                ColorBlendFunction = BlendFunction.Add,
                ColorSourceBlend = Blend.DestinationColor,
                ColorDestinationBlend = Blend.Zero
            };

            mCutoutState = new BlendState()
            {
                ColorBlendFunction = BlendFunction.Add,
                ColorSourceBlend = Blend.Zero,
                ColorDestinationBlend = Blend.InverseSourceAlpha,
                AlphaBlendFunction = BlendFunction.Add,
                AlphaSourceBlend = Blend.Zero,
                AlphaDestinationBlend = Blend.InverseSourceAlpha
            };

            mGameWindow = monoGame.Window;
        }

        public IList<string> ValidVideoModes => GetValidVideoModes();

        public void UpdateGraphicsState(int width, int height)
        {
            var currentDisplayMode = mGraphics.GraphicsDevice.Adapter.CurrentDisplayMode;

            if (Globals.Database.FullScreen)
            {
                var supported = false;
                foreach (var mode in mGraphics.GraphicsDevice.Adapter.SupportedDisplayModes)
                {
                    if (mode.Width == width && mode.Height == height)
                    {
                        supported = true;
                    }
                }

                if (!supported)
                {
                    Globals.Database.FullScreen = false;
                    Globals.Database.SavePreferences();
                    Gui.MsgboxErrors.Add(new KeyValuePair<string, string>(Strings.Errors.displaynotsupported,Strings.Errors.displaynotsupportederror.ToString(width + "x" + height)));
                }
            }

            var fsChanged = mGraphics.IsFullScreen != Globals.Database.FullScreen && !Globals.Database.FullScreen;

            mGraphics.IsFullScreen = Globals.Database.FullScreen;
            if (fsChanged) mGraphics.ApplyChanges();
            mScreenWidth = width;
            mScreenHeight = height;
            mGraphics.PreferredBackBufferWidth = width;
            mGraphics.PreferredBackBufferHeight = height;
            mGraphics.SynchronizeWithVerticalRetrace = (Globals.Database.TargetFps == 0);

            if (Globals.Database.TargetFps == 1)
            {
                mGame.TargetElapsedTime = new TimeSpan(333333);
            }
            else if (Globals.Database.TargetFps == 2)
            {
                mGame.TargetElapsedTime = new TimeSpan(333333 / 2);
            }
            else if (Globals.Database.TargetFps == 3)
            {
                mGame.TargetElapsedTime = new TimeSpan(333333 / 3);
            }
            else if (Globals.Database.TargetFps == 4)
            {
                mGame.TargetElapsedTime = new TimeSpan(333333 / 4);
            }
            mGame.IsFixedTimeStep = Globals.Database.TargetFps > 0;

            mGraphics.ApplyChanges();

            mDisplayWidth = currentDisplayMode.Width;
            mDisplayHeight = currentDisplayMode.Height;
            mGameWindow.Position = new Microsoft.Xna.Framework.Point((mDisplayWidth - mScreenWidth) / 2,
                (mDisplayHeight - mScreenHeight) / 2);
            mOldDisplayMode = currentDisplayMode;
            if (fsChanged) mFsChangedTimer = Globals.System.GetTimeMs() + 1000;
            if (fsChanged) mDisplayModeChanged = true;
        }

        public void CreateWhiteTexture()
        {
            mWhiteTexture = CreateRenderTexture(1, 1);
            mWhiteTexture.Begin();
            mWhiteTexture.Clear(Framework.GenericClasses.Color.White);
            mWhiteTexture.End();
        }

        public override bool Begin()
        {
            //mGraphicsDevice.SetRenderTarget(null);
            if (mFsChangedTimer > -1 && mFsChangedTimer < Globals.System.GetTimeMs())
            {
                mGraphics.PreferredBackBufferWidth--;
                mGraphics.ApplyChanges();
                mGraphics.PreferredBackBufferWidth++;
                mGraphics.ApplyChanges();
                mFsChangedTimer = -1;
            }
            if (mGameWindow.ClientBounds.Width != 0 && mGameWindow.ClientBounds.Height != 0 &&
                (mGameWindow.ClientBounds.Width != mScreenWidth || mGameWindow.ClientBounds.Height != mScreenHeight ||
                 mGraphics.GraphicsDevice.Adapter.CurrentDisplayMode != mOldDisplayMode) &&
                !mGraphics.IsFullScreen)
            {
                if (mOldDisplayMode != mGraphics.GraphicsDevice.DisplayMode) mDisplayModeChanged = true;
                UpdateGraphicsState(mScreenWidth, mScreenHeight);
            }

            StartSpritebatch(mCurrentView, GameBlendModes.None, null, null, true, null);

            return true;
        }

        public Pointf GetMouseOffset()
        {
            return new Pointf(mGraphics.PreferredBackBufferWidth / (float) mGameWindow.ClientBounds.Width,
                mGraphics.PreferredBackBufferHeight / (float) mGameWindow.ClientBounds.Height);
        }

        private void StartSpritebatch(FloatRect view, GameBlendModes mode = GameBlendModes.None,
            GameShader shader = null, GameRenderTexture target = null, bool forced = false, RasterizerState rs = null,
            bool drawImmediate = false)
        {
            var viewsDiff = view.X != mCurrentSpriteView.X || view.Y != mCurrentSpriteView.Y ||
                             view.Width != mCurrentSpriteView.Width || view.Height != mCurrentSpriteView.Height;
            if (mode != mCurrentBlendmode || (shader != mCurrentShader || (shader != null && shader.ValuesChanged())) ||
                target != mCurrentTarget || viewsDiff ||
                forced || drawImmediate || !mSpriteBatchBegan)
            {
                if (mSpriteBatchBegan) mSpriteBatch.End();
                if (target != null)
                {
                    mGraphicsDevice?.SetRenderTarget((RenderTarget2D) target.GetTexture());
                }
                else
                {
                    mGraphicsDevice?.SetRenderTarget(mScreenshotRenderTarget);
                }
                var blend = mNormalState;
                Effect useEffect = null;

                switch (mode)
                {
                    case GameBlendModes.None:
                        blend = mNormalState;
                        break;
                    case GameBlendModes.Alpha:
                        blend = BlendState.AlphaBlend;
                        break;
                    case (GameBlendModes.Multiply):
                        blend = mMultiplyState;
                        break;
                    case (GameBlendModes.Add):
                        blend = BlendState.Additive;
                        break;
                    case (GameBlendModes.Opaque):
                        blend = BlendState.Opaque;
                        break;
                    case GameBlendModes.Cutout:
                        blend = mCutoutState;
                        break;
                }

                if (shader != null)
                {
                    useEffect = (Effect) shader.GetShader();
                    shader.ResetChanged();
                }
                mSpriteBatch.Begin(drawImmediate ? SpriteSortMode.Immediate : SpriteSortMode.Deferred, blend,
                    null, null, rs, useEffect,
                    Matrix.CreateRotationZ(0f) * Matrix.CreateScale(new Vector3(1, 1, 1)) *
                    Matrix.CreateTranslation(-view.X, -view.Y, 0));
                mCurrentSpriteView = view;
                mCurrentBlendmode = mode;
                mCurrentShader = shader;
                mCurrentTarget = target;
                mSpriteBatchBegan = true;
            }
        }

        public override bool DisplayModeChanged()
        {
            var changed = mDisplayModeChanged;
            mDisplayModeChanged = false;
            return changed;
        }

        public void EndSpriteBatch()
        {
            if (mSpriteBatchBegan)
            {
                mSpriteBatch.End();
            }

            mSpriteBatchBegan = false;
        }

        public static Microsoft.Xna.Framework.Color ConvertColor(Framework.GenericClasses.Color clr)
        {
            return new Microsoft.Xna.Framework.Color(clr.R, clr.G, clr.B, clr.A);
        }

        public override void Clear(Framework.GenericClasses.Color color)
        {
            mGraphicsDevice.Clear(ConvertColor(color));
        }

        public override void DrawTileBuffer(GameTileBuffer buffer)
        {
            EndSpriteBatch();
            mGraphicsDevice?.SetRenderTarget(mScreenshotRenderTarget);
            mGraphicsDevice.BlendState = mNormalState;
            mGraphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;
            mGraphicsDevice.SamplerStates[0] = SamplerState.LinearClamp;
            mGraphicsDevice.DepthStencilState = DepthStencilState.None;

            ((MonoTileBuffer) buffer).Draw(mBasicEffect, mCurrentView);
        }

        public override void Close()
        {
        }

        public override GameTexture GetWhiteTexture()
        {
            return mWhiteTexture;
        }

        public ContentManager GetContentManager()
        {
            return mContentManager;
        }

        public override GameRenderTexture CreateRenderTexture(int width, int height)
        {
            return new MonoRenderTexture(mGraphicsDevice, width, height);
        }

        public override void DrawString(string text, GameFont gameFont, float x, float y, float fontScale,
            Framework.GenericClasses.Color fontColor, bool worldPos = true, GameRenderTexture renderTexture = null, Framework.GenericClasses.Color borderColor = null)
        {
            if (gameFont == null) return;
            var font = (SpriteFont) gameFont.GetFont();
            if (font == null) return;
            StartSpritebatch(mCurrentView, GameBlendModes.None, null, renderTexture, false, null);
            foreach (var chr in text)
            {
                if (!font.Characters.Contains(chr))
                {
                    text = text.Replace(chr, ' ');
                }
            }
            if (borderColor != null && borderColor != Framework.GenericClasses.Color.Transparent)
            {
                mSpriteBatch.DrawString(font, text, new Vector2(x, y - 1), ConvertColor(borderColor), 0f,
                    Vector2.Zero,
                    new Vector2(fontScale, fontScale), SpriteEffects.None, 0);
                mSpriteBatch.DrawString(font, text, new Vector2(x - 1, y), ConvertColor(borderColor), 0f,
                    Vector2.Zero,
                    new Vector2(fontScale, fontScale), SpriteEffects.None, 0);
                mSpriteBatch.DrawString(font, text, new Vector2(x + 1, y), ConvertColor(borderColor), 0f,
                    Vector2.Zero,
                    new Vector2(fontScale, fontScale), SpriteEffects.None, 0);
                mSpriteBatch.DrawString(font, text, new Vector2(x, y + 1), ConvertColor(borderColor), 0f,
                    Vector2.Zero,
                    new Vector2(fontScale, fontScale), SpriteEffects.None, 0);
            }
            mSpriteBatch.DrawString(font, text, new Vector2(x, y), ConvertColor(fontColor));
        }

        public override void DrawString(string text, GameFont gameFont, float x, float y, float fontScale,
            Framework.GenericClasses.Color fontColor, bool worldPos, GameRenderTexture renderTexture, FloatRect clipRect,
            Framework.GenericClasses.Color borderColor = null)
        {
            if (gameFont == null) return;
            x += mCurrentView.X;
            y += mCurrentView.Y;
            //clipRect.X += _currentView.X;
            //clipRect.Y += _currentView.Y;
            var font = (SpriteFont) gameFont.GetFont();
            if (font == null) return;
            var clr = ConvertColor(fontColor);

            //Copy the current scissor rect so we can restore it after
            var currentRect = mSpriteBatch.GraphicsDevice.ScissorRectangle;
            StartSpritebatch(mCurrentView, GameBlendModes.None, null, renderTexture, false, mRasterizerState,true);
            //Set the current scissor rectangle
            mSpriteBatch.GraphicsDevice.ScissorRectangle = new Microsoft.Xna.Framework.Rectangle((int) clipRect.X,
                (int) clipRect.Y, (int) clipRect.Width, (int) clipRect.Height);

            foreach (var chr in text)
            {
                if (!font.Characters.Contains(chr))
                {
                    text = text.Replace(chr, ' ');
                }
            }
            if (borderColor != null && borderColor != Framework.GenericClasses.Color.Transparent)
            {
                mSpriteBatch.DrawString(font, text, new Vector2(x, y - 1), ConvertColor(borderColor), 0f,
                    Vector2.Zero,
                    new Vector2(fontScale, fontScale), SpriteEffects.None, 0);
                mSpriteBatch.DrawString(font, text, new Vector2(x - 1, y), ConvertColor(borderColor), 0f,
                    Vector2.Zero,
                    new Vector2(fontScale, fontScale), SpriteEffects.None, 0);
                mSpriteBatch.DrawString(font, text, new Vector2(x + 1, y), ConvertColor(borderColor), 0f,
                    Vector2.Zero,
                    new Vector2(fontScale, fontScale), SpriteEffects.None, 0);
                mSpriteBatch.DrawString(font, text, new Vector2(x, y + 1), ConvertColor(borderColor), 0f,
                    Vector2.Zero,
                    new Vector2(fontScale, fontScale), SpriteEffects.None, 0);
            }
            mSpriteBatch.DrawString(font, text, new Vector2(x, y), clr, 0f, Vector2.Zero,
                new Vector2(fontScale, fontScale), SpriteEffects.None, 0);
            EndSpriteBatch();

            //Reset scissor rectangle to the saved value
            mSpriteBatch.GraphicsDevice.ScissorRectangle = currentRect;
        }

        public override GameTileBuffer CreateTileBuffer()
        {
            return new MonoTileBuffer(mGraphicsDevice);
        }

        public override void DrawTexture(GameTexture tex, float sx, float sy, float sw, float sh, float tx, float ty, float tw, float th,
            Framework.GenericClasses.Color renderColor, GameRenderTexture renderTarget = null, GameBlendModes blendMode = GameBlendModes.None,
            GameShader shader = null, float rotationDegrees = 0, bool isUi = false, bool drawImmediate = false)
        {
            var texture = tex?.GetTexture();
            if (texture == null) return;

            var pack = tex.GetTexturePackFrame();
            if (pack != null)
            {
                sx += pack.Rect.X;
                sy += pack.Rect.Y;
            }


            var origin = Vector2.Zero;
            if (Math.Abs(rotationDegrees) > 0.01)
            {
                rotationDegrees = (float) ((Math.PI / 180) * rotationDegrees);
                origin = new Vector2(sw / 2, sh / 2);
                tx += sw / 2;
                ty += sh / 2;
            }
            if (renderTarget == null)
            {
                if (isUi)
                {
                    tx += mCurrentView.X;
                    ty += mCurrentView.Y;
                }
                StartSpritebatch(mCurrentView, blendMode, shader, null, false, null, drawImmediate);

                mSpriteBatch.Draw((Texture2D)texture, new Vector2(tx, ty),
                    new XNARectangle((int)sx, (int)sy, (int)sw, (int)sh), ConvertColor(renderColor),
                    rotationDegrees, origin, new Vector2(tw/sw, th/sh), SpriteEffects.None, 0);
            }
            else
            {
                StartSpritebatch(new FloatRect(0, 0, renderTarget.GetWidth(), renderTarget.GetHeight()), blendMode,
                    shader, renderTarget, false, null, drawImmediate);
                mSpriteBatch.Draw((Texture2D)texture, new Vector2(tx, ty),
                    new XNARectangle((int)sx, (int)sy, (int)sw, (int)sh), ConvertColor(renderColor),
                    rotationDegrees, origin, new Vector2(tw / sw, th / sh), SpriteEffects.None, 0);
            }
        }

        public override void End()
        {
            EndSpriteBatch();
            mFpsCount++;
            if (mFpsTimer < Globals.System.GetTimeMs())
            {
                mFps = mFpsCount;
                mFpsCount = 0;
                mFpsTimer = Globals.System.GetTimeMs() + 1000;
                mGameWindow.Title = Strings.Main.gamename + " fps: " + mFps + " rts: " + GameRenderTexture.RenderTextureCount + " vbos: " + GameTileBuffer.TileBufferCount;
            }

            foreach (var texture in mAllTextures)
            {
                texture?.Update();
            }
        }

        public override int GetFps()
        {
            return mFps;
        }

        public override int GetScreenHeight()
        {
            return mScreenHeight;
        }

        public override int GetScreenWidth()
        {
            return mScreenWidth;
        }

        public override string GetResolutionString()
        {
            return mScreenWidth + "x" + mScreenHeight;
        }

        public override List<string> GetValidVideoModes()
        {
            if (mValidVideoModes != null) return mValidVideoModes;
            mValidVideoModes = new List<string>();

            var allowedResolutions = new[]
            {
                new Resolution(800, 600),
                new Resolution(1024, 768),
                new Resolution(1024, 720),
                new Resolution(1280, 720),
                new Resolution(1280, 768),
                new Resolution(1280, 1024),
                new Resolution(1360, 768),
                new Resolution(1366, 768),
                new Resolution(1440, 1050),
                new Resolution(1440, 900),
                new Resolution(1600, 900),
                new Resolution(1680, 1050),
                new Resolution(1920, 1080)
            };

            var displayWidth = mGraphicsDevice?.DisplayMode?.Width;
            var displayHeight = mGraphicsDevice?.DisplayMode?.Height;

            foreach (var resolution in allowedResolutions)
            {
                if (resolution.X > displayWidth) continue;
                if (resolution.Y > displayHeight) continue;
                mValidVideoModes.Add(resolution.ToString());
            }

            return mValidVideoModes;
        }

        public override FloatRect GetView()
        {
            return mCurrentView;
        }

        public override void Init()
        {
            if (mInitializing) return;
            mInitializing = true;

            var database = Globals.Database;
            var validVideoModes = GetValidVideoModes();
            var targetResolution = database.TargetResolution;

            if (targetResolution < 0 || validVideoModes?.Count <= targetResolution)
            {
                Debug.Assert(database != null, "database != null");
                database.TargetResolution = 0;
                database.SavePreference("Resolution", database.TargetResolution.ToString());
            }

            var targetVideoMode = validVideoModes?[targetResolution];
            var resolution = Resolution.Parse(targetVideoMode);
            mGraphics.PreferredBackBufferWidth = resolution.X;
            mGraphics.PreferredBackBufferHeight = resolution.Y;

            UpdateGraphicsState(mGraphics?.PreferredBackBufferWidth ?? 800,
                mGraphics?.PreferredBackBufferHeight ?? 600);

            if (mWhiteTexture == null) CreateWhiteTexture();

            mInitializing = false;
        }

        public void Init(GraphicsDevice graphicsDevice)
        {
            mGraphicsDevice = graphicsDevice;
            mBasicEffect = new BasicEffect(mGraphicsDevice);
            mBasicEffect.LightingEnabled = false;
            mBasicEffect.TextureEnabled = true;
            mSpriteBatch = new SpriteBatch(mGraphicsDevice);
        }

        public override GameFont LoadFont(string filename)
        {
            //Get font size from filename, format should be name_size.xnb or whatever
            var name =
                GameContentManager.RemoveExtension(filename)
                    .Replace(Path.Combine("resources", "fonts"), "")
                    .TrimStart(Path.DirectorySeparatorChar);
            var parts = name.Split('_');
            if (parts.Length >= 1)
            {
                if (int.TryParse(parts[parts.Length - 1], out int size))
                {
                    name = "";
                    for (var i = 0; i <= parts.Length - 2; i++)
                    {
                        name += parts[i];
                        if (i + 1 < parts.Length - 2) name += "_";
                    }
                    return new MonoFont(name, filename, size, mContentManager);
                }
            }
            return null;
        }

        public override GameShader LoadShader(string shaderName)
        {
            return new MonoShader(shaderName, mContentManager);
        }

        public override GameTexture LoadTexture(string filename)
        {
            var packFrame = GameTexturePacks.GetFrame(filename);
            if (packFrame != null)
            {
                var tx = new MonoTexture(mGraphicsDevice, filename, packFrame);
                mAllTextures.Add(tx);
                return tx;
            }
            var tex = new MonoTexture(mGraphicsDevice, filename);
            mAllTextures.Add(tex);
            return tex;
        }

        public override Pointf MeasureText(string text, GameFont gameFont, float fontScale)
        {
            if (gameFont == null) return Pointf.Empty;
            var font = (SpriteFont) gameFont.GetFont();
            if (font == null) return Pointf.Empty;
            foreach (var chr in text)
            {
                if (!font.Characters.Contains(chr))
                {
                    text = text.Replace(chr, ' ');
                }
            }
            var size = font.MeasureString(text);
            return new Pointf(size.X * fontScale, size.Y * fontScale);
        }

        public override void SetView(FloatRect view)
        {
            mCurrentView = view;

            Matrix projection;
            Matrix.CreateOrthographicOffCenter(0, view.Width, view.Height, 0, 0f, -1, out projection);
            projection.M41 += -0.5f * projection.M11;
            projection.M42 += -0.5f * projection.M22;
            mBasicEffect.Projection = projection;
            mBasicEffect.View = Matrix.CreateRotationZ(0f) * Matrix.CreateScale(new Vector3(1, 1, 1)) * Matrix.CreateTranslation(-view.X, -view.Y, 0);

            return;
        }

        private RenderTarget2D mScreenshotRenderTarget;

        public override bool BeginScreenshot()
        {
            if (mGraphicsDevice == null) return false;
            mScreenshotRenderTarget = new RenderTarget2D(mGraphicsDevice, mScreenWidth, mScreenHeight);
            return true;
        }

        public override void EndScreenshot()
        {
            if (mScreenshotRenderTarget == null) return;
            ScreenshotRequests.ForEach(screenshotRequestStream =>
            {
                if (screenshotRequestStream == null) return;
                mScreenshotRenderTarget.SaveAsPng(
                    screenshotRequestStream,
                    mScreenshotRenderTarget.Width,
                    mScreenshotRenderTarget.Height
                );
                screenshotRequestStream.Close();
            });
            ScreenshotRequests.Clear();

            if (mGraphicsDevice == null) return;
            var skippedFrame = mScreenshotRenderTarget;
            mScreenshotRenderTarget = null;
            mGraphicsDevice.SetRenderTarget(null);

            if (!Begin()) return;
            mSpriteBatch?.Draw(skippedFrame, new XNARectangle(), XNAColor.White);
            End();
        }
    }
}