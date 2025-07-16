using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Xml;
using KWADTool.KwadFormat;
using ManagedSquish;
using static KWADTool.KwadFormat.KLEIAnimation;

namespace KWADTool
{
	public static class Program
	{
		private static int Main(string[] args)
		{
			if (args.Length == 1)
			{
				if (File.Exists(args[0]))
				{
					args = new string[] { "-i", args[0], "-e", "all" };
				}
			}

			var options = new CommandLineOptions();
			if (CommandLine.Parser.Default.ParseArgumentsStrict(args, options))
			{
				try
				{
					if (options.Output == null)
					{
						options.Output = Path.GetFileName(options.Input + ".d");
					}

					KWAD kwad;
					try
					{
						using (BinaryReader reader = new BinaryReader(File.Open(options.Input, FileMode.Open)))
						{
							kwad = new KWAD(reader);
						}
					}
					catch (UnexpectedSignatureException)
					{
						Console.WriteLine("Input is not a KWAD file: Unexpected signature");
						return 1;
					}
					catch (FileNotFoundException e)
					{
						Console.WriteLine("File not found: " + e.FileName);
						return 1;
					}
					catch (IOException e)
					{
						Console.WriteLine(e);
						return 1;
					}

					switch (options.ExportType)
					{
						case ExportType.Anims:
							ExtractAnims(kwad, options);
							break;

						case ExportType.Textures:
							ExtractTextures(kwad, options);
							break;

						case ExportType.Blobs:
							ExtractBlobs(kwad, options);
							break;

						case ExportType.All:
							ExtractTextures(kwad, options);
							ExtractBlobs(kwad, options);
							ExtractAnims(kwad, options);
							break;
						default:
							throw new NotImplementedException("Unhandled ExportType: " + options.ExportType);
					}



					Console.WriteLine("Work complete!");
				}
				// ReSharper disable once CatchAllClause
				catch (Exception e)
				{
					Console.WriteLine("Extraction failed: " + e);
					Console.ReadKey();
					return 1;
				}
			}

			if (options.Pause)
				Console.ReadKey();

			return 0;
		}

		private static void ExtractAnims(KWAD kwad, CommandLineOptions options)
		{
			NameLookup.LoadMappingTable();

			var buildList = (from aliasInfo in kwad.GetAliasInfoList()
								  where kwad.GetResourceInfoList()[(int)aliasInfo.ResourceIdx].GetType().SequenceEqual(KLEIBuild.KLEI_TYPE)
								  select new
								  {
									  name = Path.GetFileNameWithoutExtension(aliasInfo.AliasPath.GetString()),
									  path = Path.GetDirectoryName(aliasInfo.AliasPath.GetString()),
									  animBld = kwad.GetResourceAt<KLEIBuild>((int)aliasInfo.ResourceIdx),
								  }).ToList();

			var animBundleList = (from aliasInfo in kwad.GetAliasInfoList()
								  where kwad.GetResourceInfoList()[(int)aliasInfo.ResourceIdx].GetType().SequenceEqual(KLEIAnimation.KLEI_TYPE)
								  select new
								  {
									  name = Path.GetFileNameWithoutExtension(aliasInfo.AliasPath.GetString()),
									  path = Path.GetDirectoryName(aliasInfo.AliasPath.GetString()),
									  animDef = kwad.GetResourceAt<KLEIAnimation>((int)aliasInfo.ResourceIdx),
								  }).ToList();

			var buildCount = buildList.Count;
			var animCount = animBundleList.Count;

			Console.WriteLine("Extracting {0} animation builds...", buildCount);

			foreach (var buildInfo in buildList)
			{
				//TODO: log any issues for a current animBundle to a corresponding log.txt file in that animBundle directory
				var animBuildOutputPath = Path.Combine(options.Output, buildInfo.path, buildInfo.name + ".anim");
				if (!string.IsNullOrWhiteSpace(animBuildOutputPath))
				{
					Directory.CreateDirectory(animBuildOutputPath);
				}

				if (buildInfo.animBld != null)
				{
					ExtractAnimationBuild(kwad, buildInfo.animBld, animBuildOutputPath, options.Output, options);
				}
			}

			Console.WriteLine("Extracting {0} animation definitions...", animCount);

			foreach (var animBundle in animBundleList)
			{
				//TODO: log any issues for a current animBundle to a corresponding log.txt file in that animBundle directory
				var animBuildOutputPath = Path.Combine(options.Output, animBundle.path, animBundle.name + ".anim");
				if (!string.IsNullOrWhiteSpace(animBuildOutputPath))
				{
					Directory.CreateDirectory(animBuildOutputPath);
				}

				if (animBundle.animDef != null)
				{
					Console.WriteLine(string.Format("\tAnimation Def \"{0}\"", animBundle.name + ".anim"));

					ExtractAnimationDef(kwad, animBundle.animDef, animBuildOutputPath, options);
				}
			}

			NameLookup.SaveMappingTable();
		}

		private static void ExtractAnimationBuild(KWAD kwad, KLEIBuild animBld, string outputPath, string texturesPath, CommandLineOptions options)
		{
			if (animBld == null)
			{
				throw new ArgumentNullException("animBld");
			}

			Console.WriteLine("\tAnimation build \"{0}\":", animBld.Name.GetString());

			XmlDocument buildXml = new XmlDocument();
			XmlElement buildElement = buildXml.CreateElement("Build");
			buildElement.SetAttribute("name", animBld.Name.GetString());
			buildXml.AppendChild(buildElement);
			var frames = animBld.GetSymbolFrames();

			foreach (var symbol in animBld.GetSymbols())
			{
				if (options.Verbose)
					Console.WriteLine("\t\tSymbol \"{0}\":", symbol.GetNameString());

				NameLookup.AddSymbolMapping(symbol.Hash, symbol.GetNameString(), false);

				XmlElement symbolElement = buildXml.CreateElement("Symbol");
				symbolElement.SetAttribute("name", symbol.GetNameString());

				uint frameNum = 0;
				for (var frameIdx = (int)symbol.FrameIdx; frameIdx < symbol.FrameIdx + symbol.FrameCount; frameIdx++)
				{

					var frame = frames[frameIdx];

					//Some frames have no references to the actual model.
					if (frame.ModelResourceIdx == uint.MaxValue)
					{
						if (options.Verbose)
							Console.WriteLine("\t\t\tSkipping Frame {0}: Model reference not found", frameIdx);

						frameNum++;
						continue;
					}

					XmlElement symbolFrameElement = buildXml.CreateElement("Frame");

					symbolFrameElement.SetAttribute("framenum", frameNum.ToString());
					symbolFrameElement.SetAttribute("duration", "1");

					var model = kwad.GetResourceAt<KLEIModel>((int)frame.ModelResourceIdx);

					var leftOffset = 0;
					var topOffset = 0;

					//We only care about meshes that are "quads"
					if (model.Mesh.VertexCount == 4)
					{
						var vertecies = model.Mesh.GetVertices();
						if (IsRectangle(vertecies[0].X, vertecies[0].Y,
							vertecies[1].X, vertecies[1].Y,
							vertecies[2].X, vertecies[2].Y,
							vertecies[3].X, vertecies[3].Y))
						{
							//Since we already know it's a rectangle it is equivalent to getting that info from a top left vertex
							leftOffset = (int)vertecies.Min(vertex => vertex.X);
							topOffset = (int)vertecies.Min(vertex => vertex.Y);
						}
					}

					var imageRelativePath = kwad.GetAliasInfoList().First(aliasInfo => aliasInfo.ResourceIdx == model.TextureResourceIdx).AliasPath.GetString();
					var imageFullPath = Path.GetFullPath(Path.Combine(texturesPath, imageRelativePath.TrimStart(@"\/".ToCharArray())));
					var baseframeImageName = Path.GetFileNameWithoutExtension(imageRelativePath);
					var frameImageName = baseframeImageName;

					int frameImageWidth;
					int frameImageHeight;

					using (var textureImage = Image.FromFile(imageFullPath))
					{
						frameImageWidth = ((textureImage.Width % 2) == 0 ? textureImage.Width : (textureImage.Width + 1)) + leftOffset * 2;
						frameImageHeight = ((textureImage.Height % 2) == 0 ? textureImage.Height : (textureImage.Height + 1)) + topOffset * 2;

						using (var bitmap = new Bitmap(frameImageWidth, frameImageHeight, PixelFormat.Format32bppArgb))
						{
							bitmap.MakeTransparent();
							using (var graphics = Graphics.FromImage(bitmap))
							{
								graphics.DrawImage(textureImage, leftOffset, topOffset);
							}

							int imagePostfix = 1;
							do
							{
								var frameImagePath = Path.GetFullPath(Path.Combine(outputPath, frameImageName + ".png"));

								if (File.Exists(frameImagePath))
								{
									using (var img = Image.FromFile(frameImagePath))
									{
										if (img.Width == frameImageWidth && img.Height == frameImageHeight)
										{
											break;
										}
										else
										{
											frameImageName = baseframeImageName + "-" + imagePostfix;
											imagePostfix++;
										}
									}

								}
								else
								{
									bitmap.Save(frameImagePath, ImageFormat.Png);
									break;
								}
							} while (true);
						}
					}

					if (options.Verbose)
						Console.WriteLine("\t\t\tBuild Frame {0}: {1} (base: {2})", frameIdx, frameImageName, baseframeImageName);

					symbolFrameElement.SetAttribute("image", frameImageName);
					symbolFrameElement.SetAttribute("w", frameImageWidth.ToString());
					symbolFrameElement.SetAttribute("h", frameImageHeight.ToString());

					symbolFrameElement.SetAttribute("x", (frame.Affine3D.GetTx() + (float)frameImageWidth / 2).ToString(CultureInfo.InvariantCulture));
					symbolFrameElement.SetAttribute("y", (frame.Affine3D.GetTy() + (float)frameImageHeight / 2).ToString(CultureInfo.InvariantCulture));

					symbolElement.AppendChild(symbolFrameElement);
					frameNum++;
				}

				buildElement.AppendChild(symbolElement);
			}

			buildXml.Save(Path.Combine(outputPath, "build.xml"));
		}

		private static void ExtractAnimationDef(KWAD kwad, KLEIAnimation animDef, string outputPath, CommandLineOptions options)
		{
			if (animDef == null)
			{
				throw new ArgumentNullException("animDef");
			}

			XmlDocument animsXml = new XmlDocument();
			XmlElement animationsElement = animsXml.CreateElement("Anims");
			animsXml.AppendChild(animationsElement);

			var frames = animDef.GetFrames();
			var instances = animDef.GetInstances();
			var transforms = animDef.GetTransforms();
			var colors = animDef.GetColours();

			foreach (var anim in animDef.GetAnimations())
			{
				var animName = anim.GetNameString().Trim().Trim('\0');

				var facingMask = anim.FacingMask;

				if (facingMask != uint.MaxValue)
				{
					animName += '_';
					if ((facingMask & 1) != 0)
					{
						animName += "E_";
					}
					if ((facingMask & 2) != 0)
					{
						animName += "NE_";
					}
					if ((facingMask & 4) != 0)
					{
						animName += "N_";
					}
					if ((facingMask & 8) != 0)
					{
						animName += "NW_";
					}
					if ((facingMask & 16) != 0)
					{
						animName += "W_";
					}
					if ((facingMask & 32) != 0)
					{
						animName += "SW_";
					}
					if ((facingMask & 64) != 0)
					{
						animName += "S_";
					}
					if ((facingMask & 128) != 0)
					{
						animName += "SE_";
					}
				}

				NameLookup.AddSymbolMapping(anim.NameHash, anim.GetNameString(), true);

				if (options.Verbose)
					Console.WriteLine("\t\tAnim \"{0}\":", animName);

				XmlElement animElement = animsXml.CreateElement("anim");
				animElement.SetAttribute("name", animName);
				animElement.SetAttribute("root", NameLookup.LookUp(anim.RootSymbolHash));
				animElement.SetAttribute("numframes", anim.FrameCount.ToString());
				animElement.SetAttribute("framerate", anim.FrameRate.ToString(CultureInfo.InvariantCulture));

				for (var frameIdx = anim.FrameIdx; frameIdx < anim.FrameIdx + anim.FrameCount; frameIdx++)
				{
					if (options.Verbose)
						Console.WriteLine("\t\t\tAnim Frame {0}", frameIdx);

					var frame = frames[frameIdx];

					var frameElement = animsXml.CreateElement("frame");
					frameElement.SetAttribute("idx", frameIdx.ToString());
					frameElement.SetAttribute("w", "0"); //// Does nothing?
					frameElement.SetAttribute("h", "0");
					frameElement.SetAttribute("x", "0");
					frameElement.SetAttribute("y", "0");

					for (var instanceIdx = frame.InstanceIdx; instanceIdx < frame.InstanceIdx + frame.InstanceCount; instanceIdx++)
					{
						var instance = instances[instanceIdx];
						var instanceElement = animsXml.CreateElement("element");

						// Symbol Name is hashed so look it up among the symbols we found during build extraction
						instanceElement.SetAttribute("name", NameLookup.LookUp(instance.SymbolHash));
						instanceElement.SetAttribute("layername", NameLookup.LookUp(instance.FolderHash));
						instanceElement.SetAttribute("parentname", NameLookup.LookUp(instance.ParentHash));
						instanceElement.SetAttribute("frame", instance.SymbolFrame.ToString());
						instanceElement.SetAttribute("depth", "0"); //// Does nothing?

						if (instance.TransformIdx != uint.MaxValue)
						{
							var transform = transforms[instance.TransformIdx];

							instanceElement.SetAttribute("m1_a", transform.GetA().ToString(CultureInfo.InvariantCulture));
							instanceElement.SetAttribute("m1_b", transform.GetB().ToString(CultureInfo.InvariantCulture));
							instanceElement.SetAttribute("m1_c", transform.GetC().ToString(CultureInfo.InvariantCulture));
							instanceElement.SetAttribute("m1_d", transform.GetD().ToString(CultureInfo.InvariantCulture));
							instanceElement.SetAttribute("m1_tx", transform.GetTx().ToString(CultureInfo.InvariantCulture));
							instanceElement.SetAttribute("m1_ty", transform.GetTy().ToString(CultureInfo.InvariantCulture));
						}

						if (instance.ParentTransformIdx != uint.MaxValue)
						{
							var transform = transforms[instance.ParentTransformIdx];

							instanceElement.SetAttribute("m0_a", transform.GetA().ToString(CultureInfo.InvariantCulture));
							instanceElement.SetAttribute("m0_b", transform.GetB().ToString(CultureInfo.InvariantCulture));
							instanceElement.SetAttribute("m0_c", transform.GetC().ToString(CultureInfo.InvariantCulture));
							instanceElement.SetAttribute("m0_d", transform.GetD().ToString(CultureInfo.InvariantCulture));
							instanceElement.SetAttribute("m0_tx", transform.GetTx().ToString(CultureInfo.InvariantCulture));
							instanceElement.SetAttribute("m0_ty", transform.GetTy().ToString(CultureInfo.InvariantCulture));
						}

						if (instance.CMIdx != uint.MaxValue || instance.CAIdx != uint.MaxValue)
						{
							instanceElement.SetAttribute("c_01", "0");
							instanceElement.SetAttribute("c_02", "0");
							instanceElement.SetAttribute("c_03", "0");

							instanceElement.SetAttribute("c_10", "0");
							instanceElement.SetAttribute("c_12", "0");
							instanceElement.SetAttribute("c_13", "0");

							instanceElement.SetAttribute("c_20", "0");
							instanceElement.SetAttribute("c_21", "0");
							instanceElement.SetAttribute("c_23", "0");

							instanceElement.SetAttribute("c_30", "0");
							instanceElement.SetAttribute("c_31", "0");
							instanceElement.SetAttribute("c_32", "0");

							instanceElement.SetAttribute("c_40", "0");
							instanceElement.SetAttribute("c_41", "0");
							instanceElement.SetAttribute("c_42", "0");
							instanceElement.SetAttribute("c_43", "0");
							instanceElement.SetAttribute("c_44", "1");

							if (instance.CMIdx != uint.MaxValue)
							{
								var color = colors[instance.CMIdx];

								instanceElement.SetAttribute("c_00", color.R.ToString(CultureInfo.InvariantCulture));
								instanceElement.SetAttribute("c_11", color.G.ToString(CultureInfo.InvariantCulture));
								instanceElement.SetAttribute("c_22", color.B.ToString(CultureInfo.InvariantCulture));
								instanceElement.SetAttribute("c_33", color.A.ToString(CultureInfo.InvariantCulture));
							}
							else
							{
								instanceElement.SetAttribute("c_00", "1");
								instanceElement.SetAttribute("c_11", "1");
								instanceElement.SetAttribute("c_22", "1");
								instanceElement.SetAttribute("c_33", "1");
							}

							if (instance.CAIdx != uint.MaxValue)
							{
								var color = colors[instance.CAIdx];

								instanceElement.SetAttribute("c_04", color.R.ToString(CultureInfo.InvariantCulture));
								instanceElement.SetAttribute("c_14", color.G.ToString(CultureInfo.InvariantCulture));
								instanceElement.SetAttribute("c_24", color.B.ToString(CultureInfo.InvariantCulture));
								instanceElement.SetAttribute("c_34", color.A.ToString(CultureInfo.InvariantCulture));
							}
							else
							{
								instanceElement.SetAttribute("c_04", "0");
								instanceElement.SetAttribute("c_14", "0");
								instanceElement.SetAttribute("c_24", "0");
								instanceElement.SetAttribute("c_34", "0");
							}
						}

						frameElement.AppendChild(instanceElement);
					}

					animElement.AppendChild(frameElement);
				}

				animationsElement.AppendChild(animElement);
			}

			animsXml.Save(Path.Combine(outputPath, "animation.xml"));
		}

		private static void ExtractBlobs(KWAD kwad, CommandLineOptions options)
		{
			var namedBlobList = (from aliasInfo in kwad.GetAliasInfoList()
								 where kwad.GetResourceInfoList()[(int)aliasInfo.ResourceIdx].GetType().SequenceEqual(KLEIBlob.KLEI_TYPE)
								 select new { name = aliasInfo.AliasPath.GetString(), blob = kwad.GetResourceAt<KLEIBlob>((int)aliasInfo.ResourceIdx) }).ToList();

			Console.WriteLine("Extracting {0} blobs...", namedBlobList.Count);

			foreach (var namedBlob in namedBlobList)
			{
				Console.WriteLine(namedBlob.name);

				var path = Path.GetFullPath(Path.Combine(options.Output, namedBlob.name.TrimStart(@"\/".ToCharArray())));

				var parentDirectory = Path.GetDirectoryName(path);
				if (!string.IsNullOrWhiteSpace(parentDirectory))
				{
					Directory.CreateDirectory(parentDirectory);
				}

				using (var fileStream = File.Open(path, FileMode.Create))
				{
					var blobData = namedBlob.blob.GetData();
					fileStream.Write(blobData, 0, blobData.Length);
				}
			}
		}

		private static void ExtractTextures(KWAD kwad, CommandLineOptions options)
		{
			var namedTextureList = (from aliasInfo in kwad.GetAliasInfoList()
									where kwad.GetResourceInfoList()[(int)aliasInfo.ResourceIdx].GetType().SequenceEqual(KLEITexture.KLEI_TYPE)
									select new { name = aliasInfo.AliasPath.GetString(), texture = kwad.GetResourceAt<KLEITexture>((int)aliasInfo.ResourceIdx) }).ToList();

			Console.WriteLine("Extracting {0} textures...", namedTextureList.Count);

			var groupedNamedTexture = namedTextureList.GroupBy(namedTexture => namedTexture.texture.ParentSurfaceResourceIdx);
			foreach (var surfaceGroup in groupedNamedTexture)
			{
				var surfaceName = kwad.GetAliasInfoList()
									  .First(aliasInfo => aliasInfo.ResourceIdx == surfaceGroup.Key)
									  .AliasPath.GetString();

				Console.WriteLine(surfaceName + ":");

				var surface = kwad.GetResourceAt<KLEISurface>((int)surfaceGroup.Key);
				var mipmap = surface.GetMipmaps()[0];
				var imageData = mipmap.CompressedSize > 0 ? DecompressMipmap(mipmap) : mipmap.GetData();

				if (surface.IsDXTCompressed)
				{
					imageData = Squish.DecompressImage(imageData, (int)mipmap.Width, (int)mipmap.Height, SquishFlags.Dxt5);
				}

				RgbaToBgra(imageData);

				GCHandle pinnedImageData = GCHandle.Alloc(imageData, GCHandleType.Pinned);
				try
				{
					IntPtr imageDataIntPtr = pinnedImageData.AddrOfPinnedObject();

					using (Bitmap image = new Bitmap((int)mipmap.Width, (int)mipmap.Height, (int)mipmap.Width * 4, PixelFormat.Format32bppArgb, imageDataIntPtr))
					{

						foreach (var namedTexture in surfaceGroup)
						{
							var texture = namedTexture.texture;

							if (options.Verbose)
								Console.WriteLine("\t{0}", namedTexture.name);

							var path = Path.GetFullPath( Path.Combine( options.Output, namedTexture.name.TrimStart(@"\/".ToCharArray()) ) );

							var parentDirectory = Path.GetDirectoryName(path);
							if (!string.IsNullOrWhiteSpace(parentDirectory))
							{
								Directory.CreateDirectory(parentDirectory);
							}

							using (var fileStream = File.Open(path, FileMode.Create))
							{
								// ReSharper disable CompareOfFloatsByEqualityOperator
								if (texture.Affine2d.TranslateX == 0 && texture.Affine2d.TranslateY == 0 &&
									texture.Affine2d.C1R2 == 0 && texture.Affine2d.C2R1 == 0 &&
									texture.Affine2d.ScaleX == 1 && texture.Affine2d.ScaleY == 1)
								{
									image.Save(fileStream, ImageFormat.Png);
								}
								else
								{
									image.Clone(new RectangleF(texture.Affine2d.TranslateX * mipmap.Width, texture.Affine2d.TranslateY * mipmap.Height,
										texture.Width, texture.Height), image.PixelFormat).Save(fileStream, ImageFormat.Png);
								}
							}
						}
					}
				}
				finally
				{
					pinnedImageData.Free();
				}
			}
		}

		private static void RgbaToBgra(byte[] abgrData)
		{
			for (int i = 0; i < abgrData.Length; i += 4)
			{
				var tmp = abgrData[i];
				abgrData[i] = abgrData[i + 2];
				abgrData[i + 2] = tmp;
			}
		}

		private static byte[] DecompressMipmap(KLEISurface.Mipmap mipmap)
		{
			var compressedMipmapData = mipmap.GetData();
			byte[] decompressedMipmapData;
			using (var memoryStream = new MemoryStream((int)mipmap.Size))
			{
				/* https://stackoverflow.com/a/21544269
				 * "The first two bytes of a raw ZLib stream provide details about the type of compression used.
				 * Microsoft's DeflateStream class in System.Io.Compression doesn't understand these."
				 * FYI: ZLib header usually starts with "78 XX"
				 */
				using (var compressedMipmapDataStream = new MemoryStream(compressedMipmapData, 2, compressedMipmapData.Length - 2))
				using (var decompressionStream = new DeflateStream(compressedMipmapDataStream, CompressionMode.Decompress))
				{
					decompressionStream.CopyTo(memoryStream);
				}

				decompressedMipmapData = memoryStream.ToArray();
			}
			return decompressedMipmapData;
		}

		//http://stackoverflow.com/a/2304031
		//KWAD model's vertex position stored as a float but the values are integers so comparison should not be a problem
		//Can have flaws when used in other context
		//TODO: Do a simpler implementation just for our context
		private static bool IsRectangle(float x1, float y1,
				 float x2, float y2,
				 float x3, float y3,
				 float x4, float y4)
		{
			double cx, cy;
			double dd1, dd2, dd3, dd4;

			cx = (x1 + x2 + x3 + x4) / 4;
			cy = (y1 + y2 + y3 + y4) / 4;

			dd1 = Math.Pow(cx - x1, 2) + Math.Pow(cy - y1, 2);
			dd2 = Math.Pow(cx - x2, 2) + Math.Pow(cy - y2, 2);
			dd3 = Math.Pow(cx - x3, 2) + Math.Pow(cy - y3, 2);
			dd4 = Math.Pow(cx - x4, 2) + Math.Pow(cy - y4, 2);

			return dd1 == dd2 && dd1 == dd3 && dd1 == dd4;
		}
	}
}