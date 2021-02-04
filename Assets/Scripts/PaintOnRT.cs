using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

public class PaintOnRT : MonoBehaviour
{
    public RawImage maskImg;
    /// <summary>
    /// 笔刷贴图
    /// </summary>
    public Texture2D brushTex;

    public Vector2 offsetFrom = new Vector2(20f, 20f);
    public Vector2 offsetTo = new Vector2(200f, 200f);

    /// <summary>
    /// 笔刷尺寸
    /// </summary>
    public float brushSize = 10f;
    /// <summary>
    /// 笔刷精度
    /// </summary>
    [Range(1f, 20f)]
    public float precision = 5f;

    private RenderTexture _rt;
    private CommandBuffer _cb;
    private bool _isDirty;
    private Vector2 _beginPos;
    private Vector2 _endPos;
    private List<Vector2> _lstDrawPts;
    
    private Mesh _quad;
    private Matrix4x4 _matrixProj;
    private Material _material;

    void Start()
    {
        if(maskImg == null)
        {
            enabled = false;
            return;
        }

        _lstDrawPts = new List<Vector2>();
        _rt = new RenderTexture(600, 600, 24, RenderTextureFormat.ARGB32,0); // TODO 改成与 Image同样尺寸
        _rt.antiAliasing = 2;
        maskImg.texture = _rt;

        Init();

        _cb = new CommandBuffer() { name = "paint cb" };
        ResetRT();
        _isDirty = false;
    }

    private void ResetRT()
    {
        _cb.Clear();
        _cb.SetRenderTarget(_rt);
        _cb.ClearRenderTarget(true, true, Color.black);
        _cb.SetViewProjectionMatrices(Matrix4x4.identity, _matrixProj);

        if(brushTex == null)
            brushTex = Texture2D.whiteTexture;
    }


    void LateUpdate()
    {
        _isDirty = true;
        if(BuildCommands())
        {
            Graphics.ExecuteCommandBuffer(_cb);
        }
    }

    private bool BuildCommands()
    {
        if (!_isDirty)
            return false;

        _lstDrawPts.Clear();

        {// test
            _beginPos = offsetFrom;
            _endPos = offsetTo;
        }

        Vector2 fromToVec = _endPos - _beginPos;
        Vector2 dir = fromToVec.normalized;
        float len = fromToVec.magnitude;

        float offset = 0;
        while(offset <= len)
        {
            Vector2 tmpPt = _beginPos + dir * offset;
            tmpPt -= Vector2.one * brushSize * 0.5f; // 将笔刷居中到绘制点
            _lstDrawPts.Add(tmpPt);
            offset += precision;
        }

        _cb.Clear();
        // Instancing Draw Points

        ResetRT();
        foreach(Vector2 pt in _lstDrawPts)
        {
            Matrix4x4 matrix = Matrix4x4.TRS(new Vector3(pt.x, pt.y, 0), Quaternion.identity, Vector3.one * brushSize);
            _cb.DrawMesh(_quad, matrix, _material);
        }

        _isDirty = false;
        return true;
    }

    private void Init()
    {
        _quad = new Mesh();
        _quad.SetVertices(new Vector3[]
        {
            new Vector3(0, 0, 0),
            new Vector3(0, 1, 0),
            new Vector3(1, 0, 0),
            new Vector3(1, 1, 0)
        });

        _quad.SetUVs(0, new Vector2[]
            {
                new Vector2(0, 0),
                new Vector2(0, 1),
                new Vector2(1, 0),
                new Vector2(1, 1)
            });

        _quad.SetIndices(new int[] { 0, 1, 2, 3, 2, 1 }, MeshTopology.Triangles, 0, false);
        _quad.UploadMeshData(true);

        _matrixProj = Matrix4x4.Ortho(0, _rt.width, 0, _rt.height, -1f, 1f);

        Shader shader = Resources.Load<Shader>("PaintOnRT");
        _material = new Material(shader);
    }
}
