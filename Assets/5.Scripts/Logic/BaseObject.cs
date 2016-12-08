﻿using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class BaseObject : PooledObject
{
    //COMPONENTS
    protected NavMeshAgent navMeshAgent;
    protected ObjectRenderer objectRenderer;
    [SerializeField] //maybe read data from JSON later for easier configuration ?
    public ObjectData objectData = new ObjectData();
    //[SerializeField] //serialize field for easier debuggin, don't need now
    protected AnimatorWrapper animatorWrapper;
    protected Animator childAnimator;
    //MANAGERS
    //[SerializeField]
    protected ObjectManager objectManager;
    protected ProjectileManager projectileManager;
    protected CameraController cameraController; //need for screen shake
    protected MouseController mouseController; //need for hero only, might change later though

    //public variables (for other unit to check
    public bool isEnemy;
    public ObjectState objectState;

    //Private variables, mostly for flagging and checking 
    protected float idleCountDown; //The amount of frame to check for new target when idle
    protected bool isDamageDeal;
    protected float attackCountUp;
    //[SerializeField]
    protected long targetID;
    protected BaseObject targetObject;
    
    protected Vector3 targetPosition;
    protected int objectChargeCountdown;

    public virtual ObjectType GetObjectType() { return ObjectType.Invalid; }

    public virtual ProjectileType GetProjectileType() { return ProjectileType.Invalid; }

    public virtual CorpseType GetCorpseType() { return CorpseType.Invalid; } 

    public virtual void Init(ObjectManager _objectManager, bool isEnemyTeam, int objectLevel)
    {
        //for testing and easier debuging.
        gameObject.name = GetObjectType().ToString() + "_" + id.ToString();
        isEnemy = isEnemyTeam;
        objectManager = _objectManager;
        OnFirstInit();
        PrepareComponent();
        
        UpdateStatsByLevel(objectLevel);
        SetState(ObjectState.Idle);
        isDamageDeal = false;
        objectChargeCountdown = GameConstant.objectChargeCountdownValue;
        idleCountDown = GameConstant.idleCheckFrequency;
        healthRegenCountUp = 0f;
    }

    protected override void OnFirstInit()
    {
        base.OnFirstInit();
        projectileManager = Directors.projectileManager;
        cameraController = Directors.cameraController;
        mouseController = Directors.mouseController;
    }

    protected virtual void PrepareComponent()
    {
        if (navMeshAgent == null)
        {
            navMeshAgent = GetComponent<NavMeshAgent>();
        }
        navMeshAgent.speed = objectData.moveSpeed;
        childAnimator = GetComponentInChildren<Animator>();
        animatorWrapper = new AnimatorWrapper(childAnimator);

        if (objectRenderer == null)
        {
            objectRenderer = GetComponentInChildren<ObjectRenderer>();
            objectRenderer.InitRenderer(this);
        }
        objectRenderer.UpdateHealthBar(1f);
    }

    //TODO: make this function read data externally
    public virtual void UpdateStatsByLevel(int level = 1)
    {
        objectData.level = level;
        objectData.sight = objectData.attackRange + 3f;
        objectData.maxHealth = objectData.baseMaxHealth+ (int)(objectData.baseMaxHealth * level * GameConstant.normalCreepDamageIncreasePerLevel);
        objectData.damange = objectData.baseDamage+ (int)(objectData.baseDamage * level * GameConstant.normalCreepDamageIncreasePerLevel);
        objectData.health = objectData.maxHealth;
    }

    //Update is called every frame by this object manager.
    public virtual void DoUpdate()
    {
        switch (objectState)
        {
            case ObjectState.Idle:
                ObjectIdle();
                break;
            case ObjectState.Run:
                ObjectRunning();
                break;
            case ObjectState.Charge:
                ObjectCharging();
                break;
            case ObjectState.Attack:
                ObjectAttack();
                break;
            case ObjectState.Stun:
                ObjectStun();
                break;
            case ObjectState.Special:
                ObjectSpecial();
                break;
            case ObjectState.Die:
                WhileObjectDie();
                break;
        }
        //update animator wrapper to make animation run correctly
        RegenHealth();
        animatorWrapper.DoUpdate();
        objectRenderer.DoUpdateRenderer();
    }

    //What should this object do when a state is changed ????
    protected void SetState(ObjectState state)
    {
        switch (state)
        {
            case ObjectState.Idle:
                animatorWrapper.AddTriggerToQueue("EnterIdleAnimation");
                break;
            case ObjectState.Run:
            case ObjectState.Charge:
                //need this one so the object will not just repeat the run animation while running
                if (objectState != ObjectState.Run && objectState != ObjectState.Charge)
                {
                    animatorWrapper.AddTriggerToQueue("EnterMovementAnimation");
                }
                break;
            case ObjectState.Attack:
                animatorWrapper.AddTriggerToQueue("EnterAttackAnimation");
                break;
            case ObjectState.Stun:
                break;
            case ObjectState.Special:
                break;
            case ObjectState.Die:
                //Maybe special case here ? to add the dead effect here and other stuffs.
                break;
        }
        objectState = state;
    }

    protected virtual void ObjectIdle()
    {
        idleCountDown -= Time.deltaTime;
        if (idleCountDown <= 0f)
        {
            idleCountDown = GameConstant.idleCheckFrequency;
            if (objectManager.RequestTarget(this) != null)
            {
                ChargeAtObject(objectManager.RequestTarget(this));
            }
        }
    }

    protected virtual void ObjectRunning()
    {
        if ((transform.position - targetPosition).magnitude <= GameConstant.runningReachingDistance)
        {
            //navMeshAgent.Stop();
            SetState(ObjectState.Idle);
        }
    }

    protected virtual void ObjectCharging()
    {
        if (IsTargetChanged())
        {
            SetState(ObjectState.Idle);
            return;
        }
        if (IsTargetInRange())
        {
            StartAttackTarget();
            return;
        } 
        
        objectChargeCountdown--;
        if (objectChargeCountdown <= 0)
        {
            objectChargeCountdown = GameConstant.objectChargeCountdownValue;
            //set the target position again.
            targetPosition = targetObject.transform.position;
            navMeshAgent.SetDestination(targetPosition);
        }
    }

    protected virtual void ObjectAttack()
    {
        //it should be attack count up, shouldn't it ? LOL
        attackCountUp += Time.deltaTime;
        if (!isDamageDeal)
        {
            if (attackCountUp >= objectData.dealDamageTime)
            {
                DealDamageToTarget();
                isDamageDeal = true;
            }
        }
        else
        {
            if (attackCountUp >= objectData.attackDuration)
            {
                FinnishAttackTarget();
            }
        }
    }

    protected virtual void ObjectStun() { }

    protected virtual void ObjectSpecial() { }

    //overide for hero
    protected virtual void WhileObjectDie() { }

    //manager call - this function is called by manager or by player control
    public void SetMovePosition(Vector3 _targetPosition)
    {
        targetPosition = _targetPosition;
        navMeshAgent.Resume();
        navMeshAgent.SetDestination(targetPosition);
        SetState(ObjectState.Run);
    }

    //manager call - this function is called by manager or by player control
    public void ChargeAtObject(BaseObject target)
    {
        if (target == null)
        {
            //just let it be idle
            SetState(ObjectState.Idle);
        }
        else
        {
            SetTargetObject(target);
            targetPosition = targetObject.transform.position;
            navMeshAgent.Resume();
            navMeshAgent.SetDestination(targetPosition);
            SetState(ObjectState.Charge);
        }
    }

    protected void SetTargetObject(BaseObject _target)
    {
        targetObject = _target;
        targetID = _target.id;
    }

    protected bool IsTargetChanged()
    {
        return targetID != targetObject.id;    
    }

    protected bool IsTargetInRange()
    {
        if (Ultilities.GetDistanceBetween(gameObject, targetObject.gameObject) <= objectData.attackRange)
        //if ((transform.position - targetPosition).magnitude <= objectData.attackRange)
        {
            return true;
        }
        return false;
    }

    protected virtual void StartAttackTarget()
    {
        if (targetObject != null && !IsTargetChanged())
        {
            transform.LookAt(targetObject.transform.position);
            SetState(ObjectState.Attack);
        }
        else
        {
            SetState(ObjectState.Idle);
            return;
        }
        navMeshAgent.Stop();
        isDamageDeal = false;
        attackCountUp = 0;
    }

    //TODO recheck this function !!!!
    protected virtual void DealDamageToTarget()
    {
        if (targetObject != null && !IsTargetChanged())
        {
            projectileManager.CreateProjectile(GetProjectileType(), isEnemy, objectData.damange, transform.position, this, targetObject.transform.position, targetObject);
            //targetObject.ReceiveDamage(10, this);
        }
    }

    protected void FinnishAttackTarget()
    {
        isDamageDeal = false;
        //The last check -> if enemy is attacking the cargo, while the hero is close, he should focus on the
        //hero instead. -> Might change later
        if (targetObject != null && targetObject.objectState != ObjectState.Die && !IsTargetChanged() && IsTargetInRange() && !isEnemy)
        {
            StartAttackTarget();
        }
        else
        {
            BaseObject requestedTarget = objectManager.RequestTarget(this);
            if (requestedTarget == null)
            {
                SetState(ObjectState.Idle);
            }
            else
            {
                ChargeAtObject(requestedTarget);
            }
        }
    }

    //Passing the attacker in to check detail
    public virtual void ReceiveDamage(int damage, BaseObject attacker = null)
    {
        if (GetHealthRegenRate() > 0f)
        {
            healthRegenCountUp = - GameConstant.attackStopRegenTime * GetHealthRegenRate() * objectData.maxHealth / 100f;
        }
        //Complex calculation here later if I want the game to get complex
        ReduceHealth(damage);
    }

    protected virtual void ReduceHealth(int damage)
    {
        objectData.health -= damage;
        objectRenderer.UpdateHealthBar();
        if (objectData.health <= 0)
        {
            OnObjectDie();
        } 
        
    }

    public virtual void OnObjectDie()
    {
        SetState(ObjectState.Die);
        DeadEffect();
        objectManager.RemoveObject(this);
        KillObject();
    }

    protected virtual void DeadEffect()
    {
        for (int i = 0; i < 9; i++)
        {
            Corpse corpse = PrefabsManager.SpawnCorpse(GetCorpseType());
            corpse.Init(transform.position);
        }
    }

    protected virtual void KillObject()
    {
        //TODO Use fucking pools man
        //Destroy(gameObject);
        ReturnToPool();
    }

    private float healthRegenCountUp = 0f;

    protected virtual void RegenHealth()
    {
        if (GetHealthRegenRate() <= 0 || objectState == ObjectState.Die || objectData.health >= objectData.maxHealth)
        {
            return;
        }
        healthRegenCountUp += Time.deltaTime * GetHealthRegenRate() * objectData.maxHealth / 100f;
        if (healthRegenCountUp >= 1)
        {
            objectData.health = Mathf.Min((int)healthRegenCountUp + objectData.health, objectData.maxHealth);
            healthRegenCountUp -= (int)healthRegenCountUp;
            objectRenderer.UpdateHealthBar();
        }
    }

    //percent of max health regen per second
    //1 mean 1% of health every second
    protected virtual float GetHealthRegenRate() {
        return 0f;
    }
}

public enum ObjectState
{
    Idle,
    Run,
    Charge,
    Attack,
    Stun,
    Special,
    Die
}