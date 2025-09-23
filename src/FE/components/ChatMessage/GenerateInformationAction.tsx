import useTranslation from '@/hooks/useTranslation';

import { formatNumberAsMoney, toFixed } from '@/utils/common';

import { IChatMessage } from '@/types/chatMessage';

import { IconInfo } from '@/components/Icons';
import { Button } from '@/components/ui/button';
import { Label } from '@/components/ui/label';
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from '@/components/ui/popover';

interface Props {
  hidden?: boolean;
  disabled?: boolean;
  message: IChatMessage;
}

export const GenerateInformationAction = (props: Props) => {
  const { t } = useTranslation();
  const { message, hidden, disabled } = props;

  const GenerateInformation = (props: { name: string; value: string }) => {
    const { name, value } = props;
    return (
      <Label key={name} className="text-xs">
        {t(name)}
        {': '}
        {value}
      </Label>
    );
  };

  const Render = () => {
    return (
      <Popover>
        <PopoverTrigger asChild>
          <Button
            disabled={disabled}
            variant="ghost"
            className="p-1 m-0 h-7 w-7"
            onClick={(e) => {
              e.stopPropagation();
            }}
          >
            <IconInfo />
          </Button>
        </PopoverTrigger>
        <PopoverContent 
          side="bottom" 
          className="w-auto p-3"
        >
            <div className="w-50">
              <div className="grid gap-4">
                <div className="pt-1 pb-2">
                  <Label className="font-medium">
                    {t('Generate information')}
                  </Label>
                </div>
              </div>
              <div className="grid">
                <div className="grid grid-cols-1 items-center">
                  <GenerateInformation
                    name={'total duration'}
                    value={message.duration?.toLocaleString() + 'ms'}
                  />
                  <GenerateInformation
                    name={'first token latency'}
                    value={message.firstTokenLatency?.toLocaleString() + 'ms'}
                  />
                  <GenerateInformation
                    name={'prompt tokens'}
                    value={`${message.inputTokens?.toLocaleString()}`}
                  />
                  <GenerateInformation
                    name={'response tokens'}
                    value={`${(
                      message.outputTokens - message.reasoningTokens
                    ).toLocaleString()}`}
                  />
                  {!!message.reasoningTokens && (
                    <GenerateInformation
                      name={'reasoning tokens'}
                      value={`${message.reasoningTokens.toLocaleString()}`}
                    />
                  )}
                  <GenerateInformation
                    name={'response speed'}
                    value={
                      message.duration
                        ? toFixed(
                            (message.outputTokens / (message.duration || 0)) *
                              1000,
                          ) + ' token/s'
                        : '-'
                    }
                  />
                  {message.inputPrice > 0 && (
                    <GenerateInformation
                      name={'prompt cost'}
                      value={'￥' + formatNumberAsMoney(+message.inputPrice, 6)}
                    />
                  )}
                  {message.outputPrice > 0 && (
                    <GenerateInformation
                      name={'response cost'}
                      value={'￥' + formatNumberAsMoney(+message.outputPrice, 6)}
                    />
                  )}
                  <GenerateInformation
                    name={'total cost'}
                    value={
                      '￥' +
                      formatNumberAsMoney(
                        +message.inputPrice + +message.outputPrice,
                        6,
                      )
                    }
                  />
                </div>
              </div>
            </div>
        </PopoverContent>
      </Popover>
    );
  };

  return <>{!hidden && Render()}</>;
};

export default GenerateInformationAction;
