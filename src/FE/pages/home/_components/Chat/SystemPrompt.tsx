import {
  FC,
  KeyboardEvent,
  useCallback,
  useEffect,
  useRef,
  useState,
} from 'react';

import useTranslation from '@/hooks/useTranslation';

import { formatPrompt } from '@/utils/promptVariable';

import { AdminModelDto } from '@/types/adminApis';
import { Prompt, PromptSlim } from '@/types/prompt';

import { IconMessage } from '@/components/Icons';
import PromptList from './PromptList';
import VariableModal from './VariableModal';

import { getUserPromptDetail } from '@/apis/clientApis';

interface Props {
  currentPrompt: string | null;
  prompts: PromptSlim[];
  model: AdminModelDto;
  onChangePromptText: (prompt: string) => void;
  onChangePrompt: (prompt: Prompt) => void;
}

const SystemPrompt: FC<Props> = ({
  currentPrompt,
  prompts,
  model,
  onChangePromptText,
  onChangePrompt,
}) => {
  const { t } = useTranslation();

  const [rawValue, setRawValue] = useState<string>(''); // 原始内容（未格式化）
  const [isEditing, setIsEditing] = useState(false); // 编辑模式状态
  const [activePromptIndex, setActivePromptIndex] = useState(0);
  const [showPromptList, setShowPromptList] = useState(false);
  const [promptInputValue, setPromptInputValue] = useState('');
  const [variables, setVariables] = useState<string[]>([]);
  const [isModalVisible, setIsModalVisible] = useState(false);

  // 获取渲染文本的函数
  const getRenderedText = () => {
    return formatPrompt(rawValue, { model });
  };

  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const promptListRef = useRef<HTMLUListElement | null>(null);

  const filteredPrompts = prompts.filter((prompt) =>
    prompt.name.toLowerCase().includes(promptInputValue.toLowerCase()),
  );

  const handleChange = (e: React.ChangeEvent<HTMLTextAreaElement>) => {
    const inputValue = e.target.value;

    setRawValue(inputValue);
    updatePromptListVisibility(inputValue);

    onChangePromptText(inputValue);
  };

  const handleInitModal = () => {
    const selectedPrompt = filteredPrompts[activePromptIndex];
    selectedPrompt &&
      getUserPromptDetail(selectedPrompt.id).then((data) => {
        setRawValue((prevContent: string) => {
          return prevContent?.replace(/\/\w*$/, data.content);
        });
        handlePromptSelect(data);
        setShowPromptList(false);
        onChangePrompt(data);
      });
  };

  const parseVariables = (content: string) => {
    const regex = /{{(.*?)}}/g;
    const foundVariables = [];
    let match;

    while ((match = regex.exec(content)) !== null) {
      foundVariables.push(match[1]);
    }

    return foundVariables;
  };

  const updatePromptListVisibility = useCallback((text: string) => {
    const match = text.match(/\/\w*$/);
    if (match) {
      setShowPromptList(true);
      setPromptInputValue(match[0].slice(1));
    } else {
      setShowPromptList(false);
      setPromptInputValue('');
    }
  }, []);

  const handlePromptSelect = (prompt: Prompt) => {
    const parsedVariables = parseVariables(prompt.content);
    setVariables(parsedVariables);

    if (parsedVariables.length > 0) {
      setIsModalVisible(true);
    } else {
      const updatedContent = rawValue?.replace(/\/\w*$/, prompt.content);

      onChangePromptText(updatedContent);
      setRawValue(updatedContent);

      updatePromptListVisibility(prompt.content);
    }
  };

  const handleSubmit = (updatedVariables: string[]) => {
    const newContent = rawValue?.replace(/{{(.*?)}}/g, (_: string, variable: string) => {
      const index = variables.indexOf(variable);
      return updatedVariables[index];
    });

    setRawValue(newContent);
    onChangePromptText(newContent);

    // 进入编辑模式以便用户继续编辑
    setIsEditing(true);
  };

  const handleKeyDown = (e: KeyboardEvent<HTMLTextAreaElement>) => {
    if (showPromptList) {
      if (e.key === 'ArrowDown') {
        e.preventDefault();
        setActivePromptIndex((prevIndex) =>
          prevIndex < prompts.length - 1 ? prevIndex + 1 : prevIndex,
        );
      } else if (e.key === 'ArrowUp') {
        e.preventDefault();
        setActivePromptIndex((prevIndex) =>
          prevIndex > 0 ? prevIndex - 1 : prevIndex,
        );
      } else if (e.key === 'Tab') {
        e.preventDefault();
        setActivePromptIndex((prevIndex) =>
          prevIndex < prompts.length - 1 ? prevIndex + 1 : 0,
        );
      } else if (e.key === 'Enter') {
        e.preventDefault();
        handleInitModal();
      } else if (e.key === 'Escape') {
        e.preventDefault();
        setShowPromptList(false);
      } else {
        setActivePromptIndex(0);
      }
    }
  };

  useEffect(() => {
    if (isEditing && textareaRef && textareaRef.current) {
      textareaRef.current.style.height = 'inherit';
      textareaRef.current.style.height = `${textareaRef.current?.scrollHeight}px`;
    }
  }, [rawValue, isEditing]);

  useEffect(() => {
    const rawContent = currentPrompt || '';
    setRawValue(rawContent);
  }, [currentPrompt, model]);

  // 移除了模式切换的useEffect，因为displayValue现在是计算值

  useEffect(() => {
    const handleOutsideClick = (e: MouseEvent) => {
      if (
        promptListRef.current &&
        !promptListRef.current.contains(e.target as Node)
      ) {
        setShowPromptList(false);
      }
    };

    window.addEventListener('click', handleOutsideClick);

    return () => {
      window.removeEventListener('click', handleOutsideClick);
    };
  }, []);

  return (
    <div className="flex flex-col">
      <label className="mb-2 text-left text-neutral-700 dark:text-neutral-400 flex gap-1 items-center">
        <IconMessage size={16} />
        {t('System Prompt')}
      </label>
      {isEditing ? (
        <textarea
          ref={textareaRef}
          className="w-full rounded-lg border border-neutral-200 bg-transparent px-4 py-3 text-neutral-900 dark:border-neutral-600 dark:text-neutral-100"
          style={{
            resize: 'none',
            bottom: `${textareaRef?.current?.scrollHeight}px`,
            maxHeight: '300px',
            overflow: `${
              textareaRef.current && textareaRef.current.scrollHeight > 400
                ? 'auto'
                : 'hidden'
            }`,
            fontFamily: 'Consolas, "Courier New", monospace',
          }}
          placeholder={
            t(`Enter a prompt or type "/" to select a prompt...`) || ''
          }
          value={rawValue || ''}
          rows={1}
          onChange={handleChange}
          onKeyDown={handleKeyDown}
          onBlur={() => {
            setIsEditing(false);
            setShowPromptList(false);
          }}
          autoFocus
        />
      ) : (
        <div
          className="w-full rounded-lg border border-neutral-200 bg-transparent px-4 py-3 text-neutral-900 dark:border-neutral-600 dark:text-neutral-100 cursor-text min-h-[2.75rem]"
          style={{
            maxHeight: '300px',
            overflow: 'auto',
            whiteSpace: 'pre-wrap',
            wordBreak: 'break-word',
          }}
          onClick={() => setIsEditing(true)}
        >
          {getRenderedText() || (
            <span className="text-neutral-400">
              {t(`Enter a prompt or type "/" to select a prompt...`) || ''}
            </span>
          )}
        </div>
      )}

      {isEditing && showPromptList && filteredPrompts.length > 0 && (
        <div>
          <PromptList
            activePromptIndex={activePromptIndex}
            prompts={filteredPrompts}
            onSelect={handleInitModal}
            onMouseOver={setActivePromptIndex}
            promptListRef={promptListRef}
          />
        </div>
      )}

      {isModalVisible && (
        <VariableModal
          prompt={prompts[activePromptIndex]}
          variables={variables}
          onSubmit={handleSubmit}
          onClose={() => setIsModalVisible(false)}
        />
      )}
    </div>
  );
};

export default SystemPrompt;
