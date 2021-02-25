/*
 * Author:Misaka-Mikoto
 * Date: 2021-02-08
 * Url:https://github.com/Misaka-Mikoto-Tech/ScratchImage
 */

using System;
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
    public struct StatData
    {
        public float    fillPercent;  // 填充百分比（非0值）
        public float    avgVal;       // 平均值
    }

    /// <summary>
    /// 直方图桶的数量，必须与shader中定义的一致, 且小于256
    /// </summary>
    public const int HISTOGRAM_BINS = 128;
    /// <summary>
    /// 用来控制透明度的RT相比Image尺寸的比例，值越小性能越高，但是精度和效果也越差
    /// </summary>
    public const float ALPHA_RT_SCALE = 0.4f;
    /// <summary>
    /// 每一批次的实例数量上限（太多有些设备会有异常）
    /// </summary>
    public const int INSTANCE_COUNT_PER_BATCH = 200;

    public Camera uiCamera;
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
    [Range(1f, 200f)]
    public float brushSize = 50f;
    /// <summary>
    /// 绘制步进精度(值过大会变成点链，过小则有性能压力)
    /// TODO 改成根据brushSize自动计算
    /// </summary>
    [Range(1f, 20f)]
    public float paintStep = 5f;
    /// <summary>
    /// 笔刷移动检测阈值
    /// </summary>
    [Range(1f, 10f)]
    public float moveThreshhold = 2f;
    /// <summary>
    /// 笔刷不透明度
    /// </summary>
    [Range(0f, 1f)]
    public float brushAlpha = 1f;
    /// <summary>
    /// 绘图材质
    /// </summary>
    public Material paintMaterial;
    /// <summary>
    /// 用来生成直方图数据的shader
    /// </summary>
    public ComputeShader histogramShader;

    /// <summary>
    /// 直方图数据
    /// </summary>
    private uint[]          _histogramData;
    private ComputeBuffer   _histogramBuffer;
    private int             _clearShaderKrnl;
    private int             _histogramShaderKrnl;
    private Vector2Int      _histogramShaderGroupSize;

    private RenderTexture   _rt;
    private CommandBuffer   _cb;
    private bool            _isDirty;
    private Vector2         _beginPos;
    private Vector2         _endPos;
    
    private Mesh            _quad;
    private Matrix4x4       _matrixProj;
    private Matrix4x4[]     _arrInstancingMatrixs;

    private int             _propIDMainTex;
    private int             _propIDBrushAlpha;
    private Vector2         _lastPoint;
    private Vector2         _maskSize;

    public Vector2 rtSize => new Vector2(_rt.width, _rt.height);


    /// <summary>
    /// 重置蒙版
    /// </summary>
    public void ResetMask()
    {
        SetupPaintContext(true);
        Graphics.ExecuteCommandBuffer(_cb);
        _isDirty = false;
    }

    /// <summary>
    /// 获取刮开的统计信息
    /// </summary>
    /// <returns></returns>
    public StatData GetStatData()
    {
        if (_histogramShaderKrnl == -1)
        {
            Debug.LogError("invalid compute shader");
            return new StatData();
        }

        histogramShader.Dispatch(_clearShaderKrnl, HISTOGRAM_BINS / _histogramShaderGroupSize.x, 1, 1);

        int dispatchX = _rt.width / _histogramShaderGroupSize.x;
        int dispatchY = _rt.height / _histogramShaderGroupSize.y;
        histogramShader.Dispatch(_histogramShaderKrnl, dispatchX, dispatchY, 1);

        // AsyncGPUReadback.Request does supported at OpenglES
        _histogramBuffer.GetData(_histogramData);

        int dispatchWidth = dispatchX * _histogramShaderGroupSize.x;
        int dispatchHeight = dispatchY * _histogramShaderGroupSize.y;
        int dispatchCount = dispatchWidth * dispatchHeight;

        StatData ret = new StatData();
        ret.fillPercent = 1.0f - _histogramData[0] / (dispatchCount * 1.0f); // 非0值比例

        float sum = 0;
        float binScale = (256 / HISTOGRAM_BINS);
        for (int i = 0; i < HISTOGRAM_BINS; i++)
        {
            int count = (int)_histogramData[i];
            sum += i * binScale * count;
        }
        ret.avgVal = sum / dispatchCount;
        // 由于桶的数量小于256，shader最大只统计到 127 * 2 = 254, 无法显示255的数据，因此此处把结果给缩放一下
        ret.avgVal *= 255.0f / ((HISTOGRAM_BINS - 1) * binScale);
        return ret;
    }

    void Start()
    {
        Init();
        ResetMask();
    }

    private void OnDestroy()
    {
        if(_rt != null)
            Destroy(_rt);

        if(_quad != null)
            Destroy(_quad);

        if (_cb != null)
            _cb.Dispose();

        if (_histogramBuffer != null)
            _histogramBuffer.Release();
    }

    private void Update()
    {
        CheckInput();
    }

    void LateUpdate()
    {
        if(BuildCommands())
        {
            Graphics.ExecuteCommandBuffer(_cb);
            _beginPos = _endPos;
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

        SetupPaintContext(false);

        while (offset <= len)
        {
            if (instCount >= INSTANCE_COUNT_PER_BATCH)
            {
                _cb.DrawMeshInstanced(_quad, 0, paintMaterial, 0, _arrInstancingMatrixs, instCount);
                instCount = 0;
            }

            Vector2 tmpPt = _beginPos + dir * offset;
            tmpPt -= Vector2.one * brushSize * 0.5f; // 将笔刷居中到绘制点
            offset += paintStep;

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
        _lastPoint = Vector2.zero;

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


        _maskSize = maskImage.rectTransform.rect.size;
        //Debug.LogFormat("mask image size:{0}*{1}", maskSize.x, maskSize.y);

        _rt = new RenderTexture((int)(_maskSize.x * ALPHA_RT_SCALE), (int)(_maskSize.y * ALPHA_RT_SCALE), 0, RenderTextureFormat.R8, 0);
        _rt.antiAliasing = 2;
        _rt.autoGenerateMips = false;

        _arrInstancingMatrixs = new Matrix4x4[INSTANCE_COUNT_PER_BATCH];
        _matrixProj = Matrix4x4.Ortho(0, _maskSize.x, 0, _maskSize.y, -1f, 1f);

        _propIDMainTex = Shader.PropertyToID("_MainTex");
        _propIDBrushAlpha = Shader.PropertyToID("_BrushAlpha");

        paintMaterial.enableInstancing = true;

        Material maskMat = maskImage.material;
        maskMat.SetTexture("_AlphaTex", _rt);

        _cb = new CommandBuffer() { name = "PaintOncb" };

        // setup histogram compute shader
        _clearShaderKrnl = -1;
        if (histogramShader != null)
        {
            _histogramBuffer = new ComputeBuffer(HISTOGRAM_BINS, 4);
            _histogramData = new uint[HISTOGRAM_BINS];

            _clearShaderKrnl = histogramShader.FindKernel("HistogramClear");
            histogramShader.SetBuffer(_clearShaderKrnl, "_HistogramBuffer", _histogramBuffer);

            _histogramShaderKrnl = histogramShader.FindKernel("Histogram");
            histogramShader.SetTexture(_histogramShaderKrnl, "_Tex", _rt);
            histogramShader.SetBuffer(_histogramShaderKrnl, "_HistogramBuffer", _histogramBuffer);

            // setup _TexScaledSize
            {
                uint x, y, z;
                histogramShader.GetKernelThreadGroupSizes(_histogramShaderKrnl, out x, out y, out z);
                uint dispatchWidth = (uint)(_rt.width / x * x);
                uint dispatchHeight = (uint)(_rt.height / y * y);

                _histogramShaderGroupSize = new Vector2Int((int)x, (int)y);

                // 要求shader执行的宽高小于真实的纹理尺寸，以避免uv溢出
                histogramShader.SetVector("_TexScaledSize", new Vector2(dispatchWidth, dispatchHeight));
            }
        }
    }

    private void SetupPaintContext(bool clearRT)
    {
        _cb.Clear();
        _cb.SetRenderTarget(_rt);

        if (clearRT)
        {
            _cb.ClearRenderTarget(true, true, Color.clear);
        }

        _cb.SetViewProjectionMatrices(Matrix4x4.identity, _matrixProj);
    }

    private void CheckInput()
    {
        if (uiCamera == null)
            return;

        int mouseStatus = 0;// 0：none, 1:down, 2:hold, 3:up

        if (Input.GetMouseButtonDown(0)) // 按下鼠标
            mouseStatus = 1;
        else if (Input.GetMouseButton(0)) // 移动鼠标或者处于按下状态
            mouseStatus = 2;
        else if (Input.GetMouseButtonUp(0)) // 释放鼠标
            mouseStatus = 3;

        if (mouseStatus == 0)
            return;

        Vector2 localPt = Vector2.zero;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(transform as RectTransform, Input.mousePosition, uiCamera, out localPt);

        //Debug.Log($"pt:{localPt}, status:{mouseStatus}");

        if (localPt.x < 0 || localPt.y < 0 || localPt.y >= _maskSize.x || localPt.y >= _maskSize.y)
            return;

        switch (mouseStatus)
        {
            case 1:
                _beginPos = localPt;
                _lastPoint = localPt;
                break;
            case 2:
                if (Vector2.Distance(localPt, _lastPoint) > moveThreshhold)
                {
                    _endPos = localPt;
                    _lastPoint = localPt;
                    _isDirty = true;
                }
                break;
            case 3:
                _endPos = localPt;
                _lastPoint = localPt;
                _isDirty = true;
                break;
        }
    }
}
