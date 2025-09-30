import useTranslation from '@/hooks/useTranslation';

import { formatNumberAsMoney, toFixed } from '@/utils/common';

import { IChatMessage, IStepGenerateInfo } from '@/types/chatMessage';

import { IconInfo } from '@/components/Icons';
import { Button } from '@/components/ui/button';
import { Label } from '@/components/ui/label';
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from '@/components/ui/popover';
import { Skeleton } from '@/components/ui/skeleton';
import { useCallback, useEffect, useState } from 'react';

interface Props {
  hidden?: boolean;
  disabled?: boolean;
  message: IChatMessage;
  chatId?: string;
  chatShareId?: string;
  isAdminView?: boolean;
  onFetchGenerateInfo?: (
    turnId: string,
    chatId?: string,
    chatShareId?: string,
  ) => Promise<IStepGenerateInfo[]>;
}

export const GenerateInformationAction = (props: Props) => {
  const { t } = useTranslation();
  const { message, hidden, disabled, chatId, chatShareId, isAdminView, onFetchGenerateInfo } = props;
  const [isOpen, setIsOpen] = useState(false);
  const [stepInfos, setStepInfos] = useState<IStepGenerateInfo[] | null>(null);
  const [loading, setLoading] = useState(false);

  const fetchGenerateInfo = useCallback(async () => {
    if (stepInfos || !onFetchGenerateInfo) return;
    
    setLoading(true);
    try {
      const infos = await onFetchGenerateInfo(message.id, chatId, chatShareId);
      setStepInfos(infos);
    } catch (error) {
      console.error('Failed to fetch generate info:', error);
    } finally {
      setLoading(false);
    }
  }, [stepInfos, onFetchGenerateInfo, message.id, chatId, chatShareId]);

  useEffect(() => {
    if (isOpen && !stepInfos && !loading) {
      fetchGenerateInfo();
    }
  }, [isOpen, stepInfos, loading, fetchGenerateInfo]);

  // 聚合步骤数据或使用消息中的旧数据
  const info = stepInfos 
    ? {
        inputTokens: stepInfos.reduce((sum, s) => sum + s.inputTokens, 0),
        outputTokens: stepInfos.reduce((sum, s) => sum + s.outputTokens, 0),
        inputPrice: stepInfos.reduce((sum, s) => sum + s.inputPrice, 0),
        outputPrice: stepInfos.reduce((sum, s) => sum + s.outputPrice, 0),
        reasoningTokens: stepInfos.reduce((sum, s) => sum + s.reasoningTokens, 0),
        duration: stepInfos.reduce((sum, s) => sum + s.duration, 0),
        reasoningDuration: stepInfos.reduce((sum, s) => sum + s.reasoningDuration, 0),
        firstTokenLatency: stepInfos[0]?.firstTokenLatency ?? 0,
      }
    : {
        inputTokens: message.inputTokens,
        outputTokens: message.outputTokens,
        inputPrice: message.inputPrice,
        outputPrice: message.outputPrice,
        reasoningTokens: message.reasoningTokens,
        duration: message.duration,
        reasoningDuration: message.reasoningDuration,
        firstTokenLatency: message.firstTokenLatency,
      };

  const GenerateInformation = (props: { 
    name: string; 
    value: string;
    icon?: string;
    loading?: boolean;
  }) => {
    const { name, value, icon, loading } = props;
    return (
      <div className="flex items-center justify-between py-0.5 px-1.5 rounded hover:bg-accent/50 transition-colors">
        <span className="text-[11px] font-medium text-muted-foreground flex items-center gap-1">
          {icon && <span className="text-xs">{icon}</span>}
          {t(name)}
        </span>
        {loading ? (
          <Skeleton className="h-3 w-20 ml-3" />
        ) : (
          <span className="text-[11px] font-semibold text-foreground ml-3">
            {value}
          </span>
        )}
      </div>
    );
  };

  const Render = () => {
    return (
      <Popover open={isOpen} onOpenChange={setIsOpen}>
        <PopoverTrigger asChild>
          <Button
            disabled={disabled}
            variant="ghost"
            className="p-1 m-0 h-7 w-7 hover:bg-accent hover:text-accent-foreground transition-colors"
            onClick={(e) => {
              e.stopPropagation();
              setIsOpen(!isOpen);
            }}
            onMouseEnter={(e) => {
              // 只在非触摸设备上启用悬停效果
              if (window.matchMedia('(hover: hover)').matches) {
                setIsOpen(true);
              }
            }}
            onMouseLeave={(e) => {
              // 只在非触摸设备上启用悬停效果
              if (window.matchMedia('(hover: hover)').matches) {
                setIsOpen(false);
              }
            }}
          >
            <IconInfo />
          </Button>
        </PopoverTrigger>
        <PopoverContent 
          side="bottom" 
          className="w-auto p-1 shadow-lg border-2"
          onPointerDownOutside={() => setIsOpen(false)}
        >
          <div className="min-w-[180px]">
            <div className="mb-2 pb-1.5 border-b">
              <Label className="text-xs font-semibold flex items-center justify-center gap-1.5">
                <span className="text-sm">📊</span>
                {t('Generate information')}
              </Label>
            </div>
            <div className="space-y-0.5">
              <GenerateInformation
                name={'total duration'}
                value={info.duration?.toLocaleString() + ' ms'}
                icon="⏱️"
                loading={loading}
              />
              <GenerateInformation
                name={'first token latency'}
                value={info.firstTokenLatency?.toLocaleString() + ' ms'}
                icon="⚡"
                loading={loading}
              />
              <GenerateInformation
                name={'prompt tokens'}
                value={`${info.inputTokens?.toLocaleString()}`}
                icon="📥"
                loading={loading}
              />
              <GenerateInformation
                name={'response tokens'}
                value={`${(
                  (info.outputTokens ?? 0) - (info.reasoningTokens ?? 0)
                ).toLocaleString()}`}
                icon="📤"
                loading={loading}
              />
              {!!(info.reasoningTokens) && (
                <GenerateInformation
                  name={'reasoning tokens'}
                  value={`${info.reasoningTokens.toLocaleString()}`}
                  icon="🧠"
                  loading={loading}
                />
              )}
              <GenerateInformation
                name={'response speed'}
                value={
                  info.duration
                    ? toFixed(
                        ((info.outputTokens ?? 0) / (info.duration || 0)) *
                          1000,
                      ) + ' token/s'
                    : '-'
                }
                icon="🚀"
                loading={loading}
              />
              {((info.inputPrice ?? 0) > 0 || (info.outputPrice ?? 0) > 0 || loading) && (
                <div className="pt-1.5 mt-1.5 border-t space-y-0.5">
                  {((info.inputPrice ?? 0) > 0 || loading) && (
                    <GenerateInformation
                      name={'prompt cost'}
                      value={'￥' + formatNumberAsMoney(+(info.inputPrice ?? 0), 6)}
                      icon="💰"
                      loading={loading}
                    />
                  )}
                  {((info.outputPrice ?? 0) > 0 || loading) && (
                    <GenerateInformation
                      name={'response cost'}
                      value={'￥' + formatNumberAsMoney(+(info.outputPrice ?? 0), 6)}
                      icon="💵"
                      loading={loading}
                    />
                  )}
                  <GenerateInformation
                    name={'total cost'}
                    value={
                      '￥' +
                      formatNumberAsMoney(
                        +(info.inputPrice ?? 0) + +(info.outputPrice ?? 0),
                        6,
                      )
                    }
                    icon="💳"
                    loading={loading}
                  />
                </div>
              )}
            </div>
          </div>
        </PopoverContent>
      </Popover>
    );
  };

  return <>{!hidden && Render()}</>;
};

export default GenerateInformationAction;
