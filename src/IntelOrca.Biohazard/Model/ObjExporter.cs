using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace IntelOrca.Biohazard.Model
{
    public class ObjExporter
    {
        private StringBuilder _sb = new StringBuilder();
        private string? _objPath;
        private double _textureWidth;
        private double _textureHeight;

        private void Begin(string objPath, int numPages)
        {
            _objPath = objPath;
            _textureWidth = numPages * 128.0;
            _textureHeight = 256.0;

            var mtlPath = Path.ChangeExtension(objPath, ".mtl");
            var imgPath = Path.ChangeExtension(objPath, ".png");
            AppendLine("newmtl main");
            AppendLine("Ka 1.000 1.000 1.000");
            AppendLine("Kd 1.000 1.000 1.000");
            AppendLine($"map_Kd {Path.GetFileName(imgPath)}");
            File.WriteAllText(mtlPath, _sb.ToString());

            _sb.Clear();
            AppendLine($"mtllib {Path.GetFileName(mtlPath)}");
            AppendLine($"usemtl main");
        }

        private void End()
        {
            File.WriteAllText(_objPath!, _sb.ToString());
        }

        public void Export(IModelMesh mesh, string objPath, int numPages, Func<int, Emr.Vector>? partTranslate = null)
        {
            var meshConverter = new MeshConverter();
            var md1 = (Md1)meshConverter.ConvertMesh(mesh, BioVersion.Biohazard2, remap: false);

            Begin(objPath, numPages);

            var vIndex = 1;
            var tvIndex = 1;
            var nIndex = 1;
            for (var partIndex = 0; partIndex < md1.NumParts; partIndex++)
            {
                var translate = partTranslate == null ? new Emr.Vector() : partTranslate(partIndex);

                var objTriangles = md1.Objects[(partIndex * 2) + 0];
                var objQuads = md1.Objects[(partIndex * 2) + 1];
                Debug.Assert(objTriangles.vtx_count == objQuads.vtx_count);
                Debug.Assert(objTriangles.nor_count == objQuads.nor_count);

                AppendLine($"o part_{partIndex:00}");

                // Triangles
                foreach (var v in md1.GetPositionData(objTriangles))
                {
                    AppendDataLine("v", (translate.x + v.x) / 1000.0, (translate.y + v.y) / 1000.0, (translate.z + v.z) / 1000.0);
                }
                foreach (var v in md1.GetNormalData(objTriangles))
                {
                    AppendDataLine("vn", v.x / 5000.0, v.y / 5000.0, v.z / 5000.0);
                }
                foreach (var t in md1.GetTriangleTextures(objTriangles))
                {
                    var page = t.page & 0x0F;
                    var offsetU = page * 128;
                    AppendDataLine("vt", (offsetU + t.u2) / _textureWidth, 1 - t.v2 / _textureHeight);
                    AppendDataLine("vt", (offsetU + t.u1) / _textureWidth, 1 - t.v1 / _textureHeight);
                    AppendDataLine("vt", (offsetU + t.u0) / _textureWidth, 1 - t.v0 / _textureHeight);
                }
                AppendLine($"s 1");
                foreach (var t in md1.GetTriangles(objTriangles))
                {
                    AppendLine($"f {t.v2 + vIndex}/{tvIndex + 0}/{t.n2 + nIndex} {t.v1 + vIndex}/{tvIndex + 1}/{t.n1 + nIndex} {t.v0 + vIndex}/{tvIndex + 2}/{t.n0 + nIndex}");
                    tvIndex += 3;
                }

                // Quads
                foreach (var t in md1.GetQuadTextures(objQuads))
                {
                    var page = t.page & 0x0F;
                    var offsetU = page * 128;
                    AppendDataLine("vt", (offsetU + t.u2) / _textureWidth, 1 - t.v2 / _textureHeight);
                    AppendDataLine("vt", (offsetU + t.u3) / _textureWidth, 1 - t.v3 / _textureHeight);
                    AppendDataLine("vt", (offsetU + t.u1) / _textureWidth, 1 - t.v1 / _textureHeight);
                    AppendDataLine("vt", (offsetU + t.u0) / _textureWidth, 1 - t.v0 / _textureHeight);
                }
                AppendLine($"s 1");
                foreach (var t in md1.GetQuads(objQuads))
                {
                    AppendLine($"f {t.v2 + vIndex}/{tvIndex + 0}/{t.n2 + nIndex} {t.v3 + vIndex}/{tvIndex + 1}/{t.n3 + nIndex} {t.v1 + vIndex}/{tvIndex + 2}/{t.n1 + nIndex} {t.v0 + vIndex}/{tvIndex + 3}/{t.n0 + nIndex}");
                    tvIndex += 4;
                }

                vIndex += objTriangles.vtx_count;
                nIndex += objTriangles.nor_count;
            }
            End();
        }

        private void AppendDataLine(string kind, params double[] parameters)
        {
            _sb.Append(kind);
            _sb.Append(' ');
            foreach (var p in parameters)
            {
                _sb.AppendFormat(CultureInfo.InvariantCulture, "{0:0.000000}", p);
                _sb.Append(' ');
            }
            _sb.Remove(_sb.Length - 1, 1);
            AppendLine();
        }

        private void AppendLine(string s)
        {
            _sb.Append(s);
            AppendLine();
        }

        private void AppendLine() => _sb.Append('\n');
    }
}
