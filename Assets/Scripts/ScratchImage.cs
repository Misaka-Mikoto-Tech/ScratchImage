using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>
/// 可以刮开的图像
/// </summary>
public class ScratchImage : MonoBehaviour
{
    /// <summary>
    /// 蒙版贴图
    /// </summary>
    public Image maskImage;
    /// <summary>
    /// 笔刷贴图
    /// </summary>
    public Texture2D brushTex;

    /// <summary>
    /// 笔刷尺寸
    /// </summary>
    public float brushSize = 10f;
    /// <summary>
    /// 笔刷精度
    /// </summary>
    [Range(1f, 20f)]
    public float precision = 5f;
    /// <summary>
    /// 笔刷不透明度
    /// </summary>
    [Range(0f, 1f)]
    public float brushAlpha = 1f;
    /// <summary>
    /// 绘图材质
    /// </summary>
    public Material paintMaterial;

    private RenderTexture _rt;
    private CommandBuffer _cb;
    private bool _isDirty;
    private Vector2 _beginPos;
    private Vector2 _endPos;
    
    private Mesh _quad;
    private Matrix4x4 _matrixProj;
    private int _instanceCountPerBatch = 200; // 每一批次的实例数量上限（太多有些设备会有异常）
    private Matrix4x4[] _arrInstancingMatrixs;

    private int _propIDMainTex;
    private int _propIDBrushAlpha;


    /// <summary>
    /// 重置蒙版
    /// </summary>
    /// <param name="clearRT"></param>
    public void ResetMask(bool clearRT)
    {
        _cb.Clear();
        _cb.SetRenderTarget(_rt);

        if (clearRT)
            _cb.ClearRenderTarget(true, true, Color.clear);

        _cb.SetViewProjectionMatrices(Matrix4x4.identity, _matrixProj);
        _isDirty = true;
    }

    void Start()
    {
        Init();
        ResetMask(true);
    }

    private void OnDestroy()
    {
        if(_rt != null)
            Destroy(_rt);

        if(_quad != null)
            Destroy(_quad);

        if (_cb != null)
            _cb.Dispose();
    }

    private void Update()
    {
        //if(Input.GetMouseButton(0))
        //{

        //}
    }

    void LateUpdate()
    {
        if(BuildCommands())
        {
            Graphics.ExecuteCommandBuffer(_cb);
        }
    }

    private bool BuildCommands()
    {
        if (!_isDirty)
            return false;

        paintMaterial.SetTexture(_propIDMainTex, brushTex != null ? brushTex : Texture2D.whiteTexture);
        paintMaterial.SetFloat(_propIDBrushAlpha, brushAlpha);

        Vector2 fromToVec = _endPos - _beginPos;
        Vector2 dir = fromToVec.normalized;
        float len = fromToVec.magnitude;

        float offset = 0;
        int instCount = 0;
        while(offset <= len)
        {
            if (instCount >= _instanceCountPerBatch)
            {
                _cb.DrawMeshInstanced(_quad, 0, paintMaterial, 0, _arrInstancingMatrixs, instCount);
                instCount = 0;
            }

            Vector2 tmpPt = _beginPos + dir * offset;
            tmpPt -= Vector2.one * brushSize * 0.5f; // 将笔刷居中到绘制点
            offset += precision;

            _arrInstancingMatrixs[instCount++] = Matrix4x4.TRS(new Vector3(tmpPt.x, tmpPt.y, 0), Quaternion.identity, Vector3.one * brushSize);
        }

        if(instCount > 0)
        {
            _cb.DrawMeshInstanced(_quad, 0, paintMaterial, 0, _arrInstancingMatrixs, instCount);
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


        Vector2 maskSize = maskImage.rectTransform.rect.size;
        //Debug.LogFormat("mask image size:{0}*{1}", maskSize.x, maskSize.y);

        _rt = new RenderTexture((int)maskSize.x, (int)maskSize.y, 0, RenderTextureFormat.R8, 0);
        _rt.antiAliasing = 2;
        _rt.autoGenerateMips = false;

        _arrInstancingMatrixs = new Matrix4x4[_instanceCountPerBatch];
        _matrixProj = Matrix4x4.Ortho(0, _rt.width, 0, _rt.height, -1f, 1f);

        _propIDMainTex = Shader.PropertyToID("_MainTex");
        _propIDBrushAlpha = Shader.PropertyToID("_BrushAlpha");

        paintMaterial.enableInstancing = true;

        Material maskMat = maskImage.material;
        maskMat.SetTexture("_AlphaTex", _rt);

        _cb = new CommandBuffer() { name = "PaintOncb" };
    }
}
