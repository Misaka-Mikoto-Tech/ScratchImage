using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>
/// ���Թο���ͼ��
/// </summary>
public class ScratchImage : MonoBehaviour
{
    public Camera uiCamera;
    /// <summary>
    /// �ɰ���ͼ
    /// </summary>
    public Image maskImage;
    /// <summary>
    /// ��ˢ��ͼ
    /// </summary>
    public Texture2D brushTex;

    /// <summary>
    /// ��ˢ�ߴ�
    /// </summary>
    public float brushSize = 10f;
    /// <summary>
    /// ��ˢ����
    /// </summary>
    [Range(1f, 20f)]
    public float precision = 5f;
    /// <summary>
    /// ��ˢ��͸����
    /// </summary>
    [Range(0f, 1f)]
    public float brushAlpha = 1f;
    /// <summary>
    /// ��ͼ����
    /// </summary>
    public Material paintMaterial;

    private RenderTexture _rt;
    private CommandBuffer _cb;
    private bool _isDirty;
    private Vector2 _beginPos;
    private Vector2 _endPos;
    
    private Mesh _quad;
    private Matrix4x4 _matrixProj;
    private int _instanceCountPerBatch = 200; // ÿһ���ε�ʵ���������ޣ�̫����Щ�豸�����쳣��
    private Matrix4x4[] _arrInstancingMatrixs;

    private int _propIDMainTex;
    private int _propIDBrushAlpha;
    private Vector2 _lastPoint;
    private Vector2 _maskSize;

    /// <summary>
    /// �����ɰ�
    /// </summary>
    public void ResetMask()
    {
        SetupPaintContext(true);
        Graphics.ExecuteCommandBuffer(_cb);
        _isDirty = false;
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
    }

    private void Update()
    {
        if (uiCamera == null)
            return;

        int mouseStatus = 0;// 0��none, 1:down, 2:hold, 3:up

        if(Input.GetMouseButtonDown(0)) // �������
            mouseStatus = 1;
        else if (Input.GetMouseButton(0)) // �ƶ������ߴ��ڰ���״̬
            mouseStatus = 2;
        else if(Input.GetMouseButtonUp(0)) // �ͷ����
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
                {
                    _beginPos = localPt;
                    _lastPoint = localPt;
                }
                break;
            case 2:
                if (Vector2.Distance(localPt, _lastPoint) > 2)
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
            if (instCount >= _instanceCountPerBatch)
            {
                _cb.DrawMeshInstanced(_quad, 0, paintMaterial, 0, _arrInstancingMatrixs, instCount);
                instCount = 0;
            }

            Vector2 tmpPt = _beginPos + dir * offset;
            tmpPt -= Vector2.one * brushSize * 0.5f; // ����ˢ���е����Ƶ�
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

        _rt = new RenderTexture((int)_maskSize.x, (int)_maskSize.y, 0, RenderTextureFormat.R8, 0);
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
}